using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Events;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Platform.Tasks;

public sealed class PlatformTaskCoordinator(
    IPlatformTaskRepository repository,
    IPalOpsEventPublisher events,
    IOptions<AppRuntimeOptions> options,
    IBackgroundWorkerSupervisor workerSupervisor,
    ILogger<PlatformTaskCoordinator> logger) : BackgroundService, IPlatformTaskCoordinator
{
    private sealed record Registration(PlatformTaskSubmission Submission, PlatformTaskHandler Handler);

    private readonly object _queueSync = new();
    private readonly PriorityQueue<string, (int Priority, long Sequence)> _queue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _resourceGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _registrationOrder = new();
    private readonly int _concurrency = Math.Clamp(options.Value.TaskCenterConcurrency, 2, 16);
    private readonly int _historyLimit = Math.Clamp(options.Value.TaskCenterHistoryLimit, 100, 10_000);
    private readonly int _defaultTimeoutSeconds = Math.Clamp(options.Value.TaskCenterDefaultTimeoutSeconds, 30, 86_400);
    private long _sequence;

    public async Task<PlatformTaskRecord> EnqueueAsync(
        PlatformTaskSubmission submission,
        PlatformTaskHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(handler);
        var type = NormalizeRequired(submission.Type, nameof(submission.Type), 80).ToLowerInvariant();
        var name = NormalizeRequired(submission.Name, nameof(submission.Name), 160);
        var now = DateTimeOffset.UtcNow;
        var record = new PlatformTaskRecord(
            NormalizeOptional(submission.TaskId, 80) ?? Guid.NewGuid().ToString("N"),
            type,
            name,
            NormalizeOptional(submission.ResourceKey, 160),
            Math.Clamp(submission.Priority, -100, 100),
            PlatformTaskStatus.Queued,
            0,
            "queued",
            "任务已进入执行队列。",
            now,
            null,
            null,
            null,
            1,
            Math.Clamp(submission.MaximumAttempts, 1, 5),
            Math.Clamp(submission.TimeoutSeconds ?? _defaultTimeoutSeconds, 5, 86_400),
            NormalizeOptional(submission.RequestedBy, 120) ?? "system",
            NormalizeOptional(submission.RemoteIp, 120) ?? "local",
            NormalizeOptional(submission.CorrelationId, 160),
            NormalizeOptional(submission.ParentTaskId, 80),
            true,
            false,
            null,
            null,
            NormalizeMetadata(submission.Metadata));

        if (_registrations.ContainsKey(record.Id) || await repository.FindAsync(record.Id, cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"任务 ID 已存在：{record.Id}");

        _registrations[record.Id] = new(submission, handler);
        _registrationOrder.Enqueue(record.Id);
        _cancellations[record.Id] = new CancellationTokenSource();
        await repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        Queue(record);
        await PublishAsync("platform.task.queued", "information", record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<PlatformTaskDashboard> GetDashboardAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        var items = await repository.ListAsync(limit, cancellationToken).ConfigureAwait(false);
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        return new(
            items,
            items.Count(static item => item.Status == PlatformTaskStatus.Queued),
            items.Count(static item => item.Status == PlatformTaskStatus.Running),
            items.Count(item => (item.Status is PlatformTaskStatus.Failed or PlatformTaskStatus.TimedOut)
                                && item.CompletedAt is { } failedAt
                                && failedAt >= dayAgo),
            items.Count(item => item.Status == PlatformTaskStatus.Completed
                                && item.CompletedAt is { } completedAt
                                && completedAt >= dayAgo),
            DateTimeOffset.UtcNow);
    }

    public Task<PlatformTaskRecord?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        repository.FindAsync(id, cancellationToken);

    public async Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status is not (PlatformTaskStatus.Queued or PlatformTaskStatus.Running)) return false;
        if (_cancellations.TryGetValue(record.Id, out var source)) source.Cancel();
        if (record.Status == PlatformTaskStatus.Queued)
        {
            var now = DateTimeOffset.UtcNow;
            record = record with
            {
                Status = PlatformTaskStatus.Cancelled,
                Stage = "cancelled",
                Message = "任务已在执行前取消。",
                CompletedAt = now,
                DurationMilliseconds = 0,
                CanCancel = false,
                CanRetry = _registrations.ContainsKey(record.Id),
                ErrorCode = "TASK_CANCELLED"
            };
            await repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
            ReleaseCancellation(record.Id);
            await PublishAsync("platform.task.cancelled", "warning", record, cancellationToken).ConfigureAwait(false);
            await TrimRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    public async Task<PlatformTaskRecord?> RetryAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null
            || !PlatformTaskStatus.IsTerminal(record.Status)
            || record.Status == PlatformTaskStatus.Completed) return null;
        if (!_registrations.ContainsKey(record.Id)) return null;

        ReleaseCancellation(record.Id);
        _cancellations[record.Id] = new CancellationTokenSource();
        record = record with
        {
            Status = PlatformTaskStatus.Queued,
            Progress = 0,
            Stage = "queued",
            Message = "任务已手动重新排队。",
            StartedAt = null,
            CompletedAt = null,
            DurationMilliseconds = null,
            Attempt = record.Attempt + 1,
            MaximumAttempts = Math.Max(record.MaximumAttempts, record.Attempt + 1),
            CanCancel = true,
            CanRetry = false,
            ErrorCode = null,
            ErrorDetail = null
        };
        await repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        Queue(record);
        await PublishAsync("platform.task.retried", "information", record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("platform-task-center", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await repository.RecoverInterruptedAsync(stoppingToken).ConfigureAwait(false);
        var workers = Enumerable.Range(0, _concurrency)
            .Select(index => RunWorkerAsync(index + 1, stoppingToken))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(int workerNumber, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            workerSupervisor.Heartbeat("platform-task-center");
            bool signaled;
            try { signaled = await _queueSignal.WaitAsync(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            if (!signaled) continue;

            string? id;
            lock (_queueSync) id = _queue.Count == 0 ? null : _queue.Dequeue();
            if (id is null) continue;
            try { await ExecuteTaskAsync(id, stoppingToken).ConfigureAwait(false); }
            catch (Exception exception) { logger.LogError(exception, "Task-center worker {Worker} failed while finalizing task {TaskId}.", workerNumber, id); }
        }
    }

    private async Task ExecuteTaskAsync(string id, CancellationToken stoppingToken)
    {
        var record = await repository.FindAsync(id, stoppingToken).ConfigureAwait(false);
        if (record is null || record.Status != PlatformTaskStatus.Queued) return;
        if (!_registrations.TryGetValue(id, out var registration))
        {
            await CompleteAsync(record, PlatformTaskStatus.Interrupted, "handler-unavailable", "任务执行器不可用。", "HANDLER_UNAVAILABLE", null, stoppingToken).ConfigureAwait(false);
            return;
        }

        SemaphoreSlim? resourceGate = null;
        var resourceAcquired = false;
        var requeued = false;
        if (!string.IsNullOrWhiteSpace(record.ResourceKey))
        {
            resourceGate = _resourceGates.GetOrAdd(record.ResourceKey, static _ => new SemaphoreSlim(1, 1));
            resourceAcquired = await resourceGate.WaitAsync(0, stoppingToken).ConfigureAwait(false);
            if (!resourceAcquired)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
                Queue(record);
                return;
            }
        }

        try
        {
            record = record with
            {
                Status = PlatformTaskStatus.Running,
                Stage = "running",
                Message = "任务正在执行。",
                StartedAt = DateTimeOffset.UtcNow,
                CanCancel = true,
                CanRetry = false
            };
            await repository.UpsertAsync(record, stoppingToken).ConfigureAwait(false);
            await PublishAsync("platform.task.started", "information", record, stoppingToken).ConfigureAwait(false);

            var taskCancellation = _cancellations.GetOrAdd(id, static _ => new CancellationTokenSource());
            using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(record.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, taskCancellation.Token, timeoutCancellation.Token);
            var context = new PlatformTaskExecutionContext(id, record.Attempt, (progress, stage, message, token) =>
                UpdateProgressAsync(id, progress, stage, message, token));

            try
            {
                await registration.Handler(context, linked.Token).ConfigureAwait(false);
                await CompleteAsync(record, PlatformTaskStatus.Completed, "completed", "任务执行完成。", null, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !taskCancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                requeued = await FailOrRetryAsync(record, PlatformTaskStatus.TimedOut, "TASK_TIMEOUT", $"任务超过 {record.TimeoutSeconds} 秒超时。", CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (taskCancellation.IsCancellationRequested)
            {
                await CompleteAsync(record, PlatformTaskStatus.Cancelled, "cancelled", "任务已取消。", "TASK_CANCELLED", null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await CompleteAsync(record, PlatformTaskStatus.Interrupted, "interrupted", "应用停止导致任务中断。", "HOST_STOPPING", null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                requeued = await FailOrRetryAsync(record, PlatformTaskStatus.Failed, "TASK_FAILED", exception.GetBaseException().Message, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (resourceAcquired) resourceGate!.Release();
            if (!requeued) ReleaseCancellation(id);
        }
    }

    private async Task<bool> FailOrRetryAsync(
        PlatformTaskRecord initial,
        string terminalStatus,
        string code,
        string detail,
        CancellationToken cancellationToken)
    {
        var latest = await repository.FindAsync(initial.Id, cancellationToken).ConfigureAwait(false) ?? initial;
        if (latest.Attempt < latest.MaximumAttempts)
        {
            latest = latest with
            {
                Status = PlatformTaskStatus.Queued,
                Progress = 0,
                Stage = "retry-wait",
                Message = $"第 {latest.Attempt} 次执行失败，准备重试。",
                StartedAt = null,
                CompletedAt = null,
                DurationMilliseconds = null,
                Attempt = latest.Attempt + 1,
                CanCancel = true,
                CanRetry = false,
                ErrorCode = code,
                ErrorDetail = detail
            };
            await repository.UpsertAsync(latest, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, latest.Attempt * 2)), cancellationToken).ConfigureAwait(false);
            Queue(latest);
            await PublishAsync("platform.task.retry-scheduled", "warning", latest, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await CompleteAsync(latest, terminalStatus, terminalStatus, detail, code, detail, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async ValueTask UpdateProgressAsync(
        string id,
        int progress,
        string? stage,
        string? message,
        CancellationToken cancellationToken)
    {
        var record = await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status != PlatformTaskStatus.Running) return;
        record = record with
        {
            Progress = Math.Clamp(progress, record.Progress, 100),
            Stage = NormalizeOptional(stage, 120) ?? record.Stage,
            Message = NormalizeOptional(message, 1000) ?? record.Message
        };
        await repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteAsync(
        PlatformTaskRecord initial,
        string status,
        string stage,
        string message,
        string? errorCode,
        string? errorDetail,
        CancellationToken cancellationToken)
    {
        var record = await repository.FindAsync(initial.Id, cancellationToken).ConfigureAwait(false) ?? initial;
        var now = DateTimeOffset.UtcNow;
        record = record with
        {
            Status = status,
            Progress = status == PlatformTaskStatus.Completed ? 100 : record.Progress,
            Stage = stage,
            Message = message,
            CompletedAt = now,
            DurationMilliseconds = record.StartedAt.HasValue ? Math.Max(0, (long)(now - record.StartedAt.Value).TotalMilliseconds) : null,
            CanCancel = false,
            CanRetry = status != PlatformTaskStatus.Completed && _registrations.ContainsKey(record.Id),
            ErrorCode = errorCode,
            ErrorDetail = errorDetail
        };
        await repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        var severity = status == PlatformTaskStatus.Completed ? "information" : status == PlatformTaskStatus.Cancelled ? "warning" : "error";
        await PublishAsync("platform.task." + status.ToLowerInvariant(), severity, record, cancellationToken).ConfigureAwait(false);
        await TrimRegistrationsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Queue(PlatformTaskRecord record)
    {
        lock (_queueSync)
        {
            _queue.Enqueue(record.Id, (-record.Priority, Interlocked.Increment(ref _sequence)));
        }
        _queueSignal.Release();
    }

    private void ReleaseCancellation(string id)
    {
        if (!_cancellations.TryRemove(id, out var source)) return;
        source.Dispose();
    }

    private async Task PublishAsync(string eventType, string severity, PlatformTaskRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await events.PublishAsync(PalOpsEvent.Create(eventType, severity, system: new Dictionary<string, object?>
            {
                ["taskId"] = record.Id,
                ["taskType"] = record.Type,
                ["taskStatus"] = record.Status,
                ["progress"] = record.Progress
            }), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to publish platform task event {EventType} for {TaskId}.", eventType, record.Id);
        }
    }

    private async Task TrimRegistrationsAsync(CancellationToken cancellationToken)
    {
        while (_registrations.Count > _historyLimit && _registrationOrder.TryDequeue(out var candidateId))
        {
            var record = await repository.FindAsync(candidateId, cancellationToken).ConfigureAwait(false);
            if (record is not null && !PlatformTaskStatus.IsTerminal(record.Status))
            {
                _registrationOrder.Enqueue(candidateId);
                break;
            }
            _registrations.TryRemove(candidateId, out _);
            ReleaseCancellation(candidateId);
        }
    }

    private static string NormalizeRequired(string value, string parameter, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("值不能为空。", parameter);
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>()
            : metadata.Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                .Take(30)
                .ToDictionary(
                    item => item.Key.Trim().Length <= 80 ? item.Key.Trim() : item.Key.Trim()[..80],
                    item => NormalizeOptional(item.Value, 500) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
}
