using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.Platform.Workers;

/// <summary>
/// Supervises long-running hosted-service loops. Unexpected exits are recorded and
/// restarted with bounded exponential backoff; host shutdown remains immediate.
/// </summary>
public sealed class BackgroundWorkerSupervisor(
    IOptions<AppRuntimeOptions> options,
    ILogger<BackgroundWorkerSupervisor> logger) : IBackgroundWorkerSupervisor
{
    private sealed class WorkerState(string name)
    {
        public object Sync { get; } = new();
        public string Name { get; } = name;
        public string Status { get; set; } = "starting";
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastHeartbeatAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastFailureAt { get; set; }
        public int RestartCount { get; set; }
        public string? LastError { get; set; }
    }

    private readonly ConcurrentDictionary<string, WorkerState> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _staleAfter = TimeSpan.FromSeconds(Math.Clamp(options.Value.WorkerStaleAfterSeconds, 30, 3600));
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(Math.Clamp(options.Value.WorkerStaleAfterSeconds / 3, 10, 60));

    public async Task RunAsync(string name, Func<CancellationToken, Task> workerLoop, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("工作器名称不能为空。", nameof(name));
        ArgumentNullException.ThrowIfNull(workerLoop);
        var normalized = name.Trim().ToLowerInvariant();
        var state = _workers.GetOrAdd(normalized, static key => new WorkerState(key));
        var failureCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            lock (state.Sync)
            {
                state.Status = failureCount == 0 ? "running" : "restarting";
                state.StartedAt = DateTimeOffset.UtcNow;
                state.LastHeartbeatAt = state.StartedAt;
            }

            try
            {
                await workerLoop(stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested) break;
                throw new InvalidOperationException("后台工作器循环意外结束。");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failureCount++;
                var now = DateTimeOffset.UtcNow;
                lock (state.Sync)
                {
                    state.Status = "failed";
                    state.LastFailureAt = now;
                    state.LastHeartbeatAt = now;
                    state.LastError = exception.GetBaseException().Message;
                    state.RestartCount++;
                }
                logger.LogError(exception, "Background worker {WorkerName} failed and will be restarted.", normalized);
                var delaySeconds = Math.Min(30, 1 << Math.Min(failureCount - 1, 5));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        lock (state.Sync)
        {
            state.Status = "stopped";
            state.LastHeartbeatAt = DateTimeOffset.UtcNow;
        }
    }

    public void Heartbeat(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var state = _workers.GetOrAdd(name.Trim().ToLowerInvariant(), static key => new WorkerState(key));
        lock (state.Sync)
        {
            state.LastHeartbeatAt = DateTimeOffset.UtcNow;
            if (state.Status is "starting" or "restarting" or "failed") state.Status = "running";
        }
    }

    public async Task DelayWithHeartbeatAsync(string name, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            Heartbeat(name);
            return;
        }

        var deadline = DateTimeOffset.UtcNow + delay;
        while (DateTimeOffset.UtcNow < deadline)
        {
            Heartbeat(name);
            var remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(remaining < _heartbeatInterval ? remaining : _heartbeatInterval, cancellationToken).ConfigureAwait(false);
        }
        Heartbeat(name);
    }

    public IReadOnlyList<BackgroundWorkerSnapshot> GetSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        return _workers.Values.Select(state =>
        {
            lock (state.Sync)
            {
                var stale = state.Status == "running" && now - state.LastHeartbeatAt > _staleAfter;
                return new BackgroundWorkerSnapshot(
                    state.Name,
                    stale ? "stale" : state.Status,
                    state.StartedAt,
                    state.LastHeartbeatAt,
                    state.LastFailureAt,
                    state.RestartCount,
                    state.LastError,
                    stale);
            }
        }).OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
