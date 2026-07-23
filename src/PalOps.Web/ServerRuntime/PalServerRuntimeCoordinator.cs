using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Audit;
using PalOps.Web.Events;
using PalOps.Web.Settings;
using PalOps.Web.Platform.Tasks;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerRuntimeCoordinator
{
    PalServerRuntimeSnapshot Current { get; }
    Task<PalServerRuntimeSnapshot> RefreshAsync(bool forceMetrics, CancellationToken cancellationToken = default);
    Task<ServerOperationSnapshot> StartAsync(string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<ServerOperationSnapshot> StopAsync(string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<ServerOperationSnapshot> RestartAsync(string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<ServerOperationSnapshot> ForceStopAsync(string userName, string remoteIp, string confirmation, string reason, CancellationToken cancellationToken = default);
    ServerOperationSnapshot? FindOperation(string operationId);
    Task HandleMonitorTransitionAsync(PalServerRuntimeSnapshot previous, PalServerRuntimeSnapshot current, CancellationToken cancellationToken = default);
    void ReportMonitorFailure(Exception exception);
}

public sealed class PalServerRuntimeCoordinator(
    IPalServerRuntimeConfigurationStore configurationStore,
    IPalServerProcessLocator locator,
    IPalServerProcessController controller,
    IPalServerShutdownService shutdown,
    IPalServerMetricsCollector metrics,
    IPalServerLiveStatusCollector liveStatus,
    IServerOperationHistoryStore history,
    IServerSettingsStore settingsStore,
    IAuditLogService audit,
    IPalOpsEventPublisher eventPublisher,
    IPlatformTaskCoordinator platformTasks,
    IOptions<AppRuntimeOptions> options,
    ILogger<PalServerRuntimeCoordinator> logger) : IPalServerRuntimeCoordinator
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, ServerOperationSnapshot> _operations = new(StringComparer.OrdinalIgnoreCase);
    private PalServerRuntimeSnapshot _current = Empty();
    private DateTimeOffset _intentionalStopUntil = DateTimeOffset.MinValue;

    public PalServerRuntimeSnapshot Current
    {
        get { lock (_sync) return _current; }
    }

    public ServerOperationSnapshot? FindOperation(string operationId) =>
        string.IsNullOrWhiteSpace(operationId) ? null : _operations.GetValueOrDefault(operationId.Trim());

    public async Task<PalServerRuntimeSnapshot> RefreshAsync(bool forceMetrics, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.GetAsync(cancellationToken);
        var process = locator.Locate(configuration);
        var before = Current;
        var shouldCollect = forceMetrics
            || before.Metrics is null
            || DateTimeOffset.UtcNow - before.Metrics.CapturedAt >= TimeSpan.FromSeconds(1);
        var metric = shouldCollect
            ? await metrics.CollectAsync(process, forceMetrics, cancellationToken)
            : before.Metrics;
        var liveStatusRefreshInterval = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.LiveStatusRefreshIntervalSeconds, 5, 60));
        var shouldCollectLive = forceMetrics
            || before.LiveStatus is null
            || DateTimeOffset.UtcNow - before.LiveStatus.CapturedAt >= liveStatusRefreshInterval;
        var live = shouldCollectLive
            ? await liveStatus.CollectAsync(process, forceMetrics, cancellationToken)
            : before.LiveStatus;
        var settings = await settingsStore.GetAsync(cancellationToken);

        lock (_sync)
        {
            var state = _current.ActiveOperation is not null
                ? _current.State
                : process.State;
            if (!forceMetrics
                && _current.State == PalServerRuntimeState.ExitedUnexpectedly.ToString()
                && !process.ProcessId.HasValue)
                state = PalServerRuntimeState.ExitedUnexpectedly.ToString();

            _current = _current with
            {
                State = state,
                Process = process,
                Metrics = metric,
                LiveStatus = live,
                ConfigurationConfirmed = configuration.Confirmed,
                RconConfigured = !string.IsNullOrWhiteSpace(settings.Rcon.Password),
                LastErrorCode = forceMetrics && _current.ActiveOperation is null ? null : _current.LastErrorCode,
                LastMessage = forceMetrics && _current.ActiveOperation is null ? null : _current.LastMessage,
                CapturedAt = DateTimeOffset.UtcNow
            };
            return _current;
        }
    }

    public Task<ServerOperationSnapshot> StartAsync(string userName, string remoteIp, CancellationToken cancellationToken = default) =>
        ScheduleAsync("start", userName, remoteIp, null, async (configuration, initialProcess, operation, token) =>
        {
            SetOperation(operation, "running", "starting-process", 25, "正在启动 PalServer", PalServerRuntimeState.Starting);
            await controller.StartAsync(configuration, token);
            SetOperation(operation, "running", "health-check", 80, "正在确认进程身份", PalServerRuntimeState.Starting);
        }, cancellationToken);

    public Task<ServerOperationSnapshot> StopAsync(string userName, string remoteIp, CancellationToken cancellationToken = default) =>
        ScheduleAsync("stop", userName, remoteIp, null, async (configuration, initialProcess, operation, token) =>
        {
            await shutdown.SafeStopAsync(configuration, initialProcess.ProcessId.Value, (stage, progress, message) =>
                SetOperation(operation, "running", stage, progress, message, PalServerRuntimeState.Stopping), token);
        }, cancellationToken);

    public Task<ServerOperationSnapshot> RestartAsync(string userName, string remoteIp, CancellationToken cancellationToken = default) =>
        ScheduleAsync("restart", userName, remoteIp, null, async (configuration, initialProcess, operation, token) =>
        {
            await shutdown.SafeStopAsync(configuration, initialProcess.ProcessId.Value, (stage, progress, message) =>
                SetOperation(operation, "running", stage, Math.Min(progress, 55), message, PalServerRuntimeState.Restarting), token);
            SetOperation(operation, "running", "restart-cooldown", 65, "正在等待重启冷却", PalServerRuntimeState.Restarting);
            await Task.Delay(TimeSpan.FromSeconds(configuration.RestartCooldownSeconds), token);
            SetOperation(operation, "running", "starting-process", 75, "正在重新启动 PalServer", PalServerRuntimeState.Restarting);
            await controller.StartAsync(configuration, token);
            SetOperation(operation, "running", "health-check", 90, "正在确认重启后的进程身份", PalServerRuntimeState.Restarting);
        }, cancellationToken);

    public Task<ServerOperationSnapshot> ForceStopAsync(
        string userName,
        string remoteIp,
        string confirmation,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, "FORCE STOP", StringComparison.Ordinal))
            throw new PalServerRuntimeException(400, "PALSERVER_FORCE_CONFIRMATION_REQUIRED", "强制停止确认文本必须精确输入 FORCE STOP。");
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500)
            throw new PalServerRuntimeException(400, "PALSERVER_FORCE_REASON_REQUIRED", "强制停止原因长度必须为 5 到 500 个字符。");

        return ScheduleAsync("force-stop", userName, remoteIp, reason, async (configuration, initialProcess, operation, token) =>
        {
            SetOperation(operation, "running", "terminating-process-tree", 60, "正在终止已确认的 PalServer 进程树", PalServerRuntimeState.Stopping);
            await controller.ForceStopAsync(initialProcess, token);
        }, cancellationToken);
    }

    public async Task HandleMonitorTransitionAsync(
        PalServerRuntimeSnapshot previous,
        PalServerRuntimeSnapshot current,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(previous.State, PalServerRuntimeState.Running.ToString(), StringComparison.Ordinal)
            || !previous.Process.IdentityVerified
            || !previous.Process.ProcessId.HasValue
            || current.Process.ProcessId.HasValue
            || current.ActiveOperation is not null
            || DateTimeOffset.UtcNow <= _intentionalStopUntil)
            return;

        var now = DateTimeOffset.UtcNow;
        var operationId = Guid.NewGuid().ToString("N");
        lock (_sync)
        {
            if (_current.State == PalServerRuntimeState.ExitedUnexpectedly.ToString()) return;
            _current = _current with
            {
                State = PalServerRuntimeState.ExitedUnexpectedly.ToString(),
                LastErrorCode = "PALSERVER_EXITED_UNEXPECTEDLY",
                LastMessage = "PalServer 进程意外退出。",
                CapturedAt = now
            };
        }

        var record = new ServerOperationHistoryRecord(
            operationId,
            "unexpected-exit",
            "failed",
            previous.CapturedAt,
            now,
            "system",
            "local",
            previous.Process.ProcessId,
            null,
            "PALSERVER_EXITED_UNEXPECTEDLY",
            "PalServer 进程意外退出。");
        await history.AppendAsync(record, cancellationToken);
        try
        {
            await audit.WriteAsync("server-runtime.unexpected-exit", "failed", "local",
                "PalServer 进程意外退出。",
                new { operationId, processId = previous.Process.ProcessId }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write unexpected-exit audit record.");
        }
        logger.LogError("PalServer process {ProcessId} exited unexpectedly.", previous.Process.ProcessId);
        PublishRuntimeEvent("server.exited-unexpectedly", "error", operationId, "unexpected-exit", "PALSERVER_EXITED_UNEXPECTEDLY", previous.Process.ProcessId, previous.Process.ExecutablePath);
    }

    public void ReportMonitorFailure(Exception exception)
    {
        lock (_sync)
        {
            if (_current.ActiveOperation is not null) return;
            _current = _current with
            {
                State = PalServerRuntimeState.Faulted.ToString(),
                LastErrorCode = "PALSERVER_MONITOR_FAILED",
                LastMessage = "运行状态监控失败。",
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
        logger.LogError(exception, "PalServer runtime monitor iteration failed.");
    }

    private async Task<ServerOperationSnapshot> ScheduleAsync(
        string type,
        string user,
        string remoteIp,
        string? reason,
        Func<PalServerRuntimeConfiguration, PalServerProcessSnapshot, ServerOperationSnapshot, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PalServerRuntimeException(409, "PALSERVER_WINDOWS_REQUIRED", "进程管理仅支持 Windows 本机部署。");
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            throw new PalServerRuntimeException(409, "PALSERVER_OPERATION_IN_PROGRESS", "已有服务器操作正在执行。");

        try
        {
            var configuration = await configurationStore.GetAsync(cancellationToken);
            if (!configuration.Confirmed)
                throw new PalServerRuntimeException(409, "PALSERVER_CONFIGURATION_NOT_CONFIRMED", "请先由 Owner 确认启动配置。");

            var process = locator.Locate(configuration);
            var identityUnknown = process.State == PalServerRuntimeState.IdentityUnknown.ToString();
            if (type == "start" && (process.ProcessId.HasValue || identityUnknown))
                throw new PalServerRuntimeException(409,
                    process.IdentityVerified ? "PALSERVER_ALREADY_RUNNING" : "PALSERVER_IDENTITY_UNKNOWN",
                    process.IdentityReason);
            if (type != "start" && !process.ProcessId.HasValue)
                throw new PalServerRuntimeException(409, "PALSERVER_NOT_RUNNING", "PalServer 当前未运行。");
            if (type != "start" && !process.IdentityVerified)
                throw new PalServerRuntimeException(409, "PALSERVER_IDENTITY_UNKNOWN", process.IdentityReason);

            var operation = new ServerOperationSnapshot(
                Guid.NewGuid().ToString("N"),
                type,
                "running",
                "queued",
                0,
                DateTimeOffset.UtcNow,
                null,
                null,
                "操作已排队");
            _operations[operation.OperationId] = operation;
            SetOperation(operation, "running", "queued", 0, "操作已排队", InitialState(type));
            await platformTasks.EnqueueAsync(
                new PlatformTaskSubmission(
                    "palserver-runtime",
                    $"PalServer {type}",
                    user,
                    remoteIp,
                    ResourceKey: "palserver-runtime",
                    Priority: 90,
                    TimeoutSeconds: Math.Clamp(Math.Max(configuration.StartupTimeoutSeconds, configuration.ShutdownTimeoutSeconds) + configuration.SaveWaitSeconds + configuration.RestartCooldownSeconds + 120, 120, 3600),
                    MaximumAttempts: 1,
                    CorrelationId: operation.OperationId,
                    Metadata: new Dictionary<string, string>
                    {
                        ["operationId"] = operation.OperationId,
                        ["operationType"] = type
                    },
                    TaskId: $"palserver-{operation.OperationId}"),
                async (taskContext, token) =>
                {
                    await taskContext.ReportProgressAsync(5, "server-operation", "PalServer 操作开始执行。", token);
                    await ExecuteAsync(configuration, process, operation, user, remoteIp, reason, action, token);
                    await taskContext.ReportProgressAsync(100, "completed", "PalServer 操作执行完成。", CancellationToken.None);
                },
                cancellationToken);
            return operation;
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    private async Task ExecuteAsync(
        PalServerRuntimeConfiguration configuration,
        PalServerProcessSnapshot initialProcess,
        ServerOperationSnapshot operation,
        string user,
        string remoteIp,
        string? reason,
        Func<PalServerRuntimeConfiguration, PalServerProcessSnapshot, ServerOperationSnapshot, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var outcome = "success";
        string? errorCode = null;
        var message = "操作完成";
        int? resultProcessId = initialProcess.ProcessId;
        try
        {
            await action(configuration, initialProcess, operation, cancellationToken);
            var process = locator.Locate(configuration);
            resultProcessId = process.ProcessId ?? resultProcessId;
            ValidateFinalState(operation.Type, process);

            if (operation.Type is "stop" or "force-stop")
                _intentionalStopUntil = DateTimeOffset.UtcNow.AddSeconds(30);

            var finalState = operation.Type is "start" or "restart"
                ? PalServerRuntimeState.Running
                : PalServerRuntimeState.Stopped;
            message = operation.Type switch
            {
                "stop" => "退出流程完成",
                "restart" => "重启流程完成",
                "start" => "启动流程完成",
                "force-stop" => "强制停止流程完成",
                _ => "操作完成"
            };
            var metric = await metrics.CollectAsync(process, true, cancellationToken);
            CompleteOperation(operation, "completed", "completed", message, finalState, null, process, metric);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            errorCode = "PALSERVER_OPERATION_CANCELLED";
            message = "PalServer 操作已取消。";
            var failureProcess = locator.Locate(configuration);
            var state = ResolveFailureState(operation.Type, errorCode, failureProcess);
            CompleteOperation(operation, "cancelled", "cancelled", message, state, errorCode, failureProcess, Current.Metrics);
            throw;
        }
        catch (PalServerRuntimeException ex)
        {
            outcome = "failed";
            errorCode = ex.Code;
            message = ex.Message;
            var failureProcess = locator.Locate(configuration);
            var state = ResolveFailureState(operation.Type, ex.Code, failureProcess);
            CompleteOperation(operation, "failed", "failed", ex.Message, state, ex.Code, failureProcess, Current.Metrics);
            logger.LogError(ex, "PalServer operation {Type} failed with {Code}.", operation.Type, ex.Code);
        }
        catch (Exception ex)
        {
            outcome = "failed";
            errorCode = "PALSERVER_OPERATION_FAILED";
            message = ex.Message;
            var failureProcess = locator.Locate(configuration);
            var state = ResolveFailureState(operation.Type, errorCode, failureProcess);
            CompleteOperation(operation, "failed", "failed", "服务器操作失败。", state,
                errorCode, failureProcess, Current.Metrics);
            logger.LogError(ex, "PalServer operation {Type} failed.", operation.Type);
        }
        finally
        {
            var completedAt = DateTimeOffset.UtcNow;
            await history.AppendAsync(new(
                operation.OperationId,
                operation.Type,
                outcome,
                operation.StartedAt,
                completedAt,
                user,
                remoteIp,
                resultProcessId,
                reason,
                errorCode,
                message), CancellationToken.None);
            try
            {
                await audit.WriteAsync(
                    "server-runtime." + operation.Type,
                    outcome,
                    remoteIp,
                    $"PalServer {operation.Type} {outcome}。",
                    new { operation.OperationId, user, processId = resultProcessId, reason, errorCode, message },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write audit record for PalServer operation {OperationId}.", operation.OperationId);
            }
            _operationGate.Release();
        }
    }

    private static PalServerRuntimeState ResolveFailureState(
        string operationType,
        string errorCode,
        PalServerProcessSnapshot process)
    {
        if (errorCode == "PALSERVER_FORCE_STOP_TIMEOUT")
            return PalServerRuntimeState.StopTimedOut;

        if (operationType is "stop" or "restart"
            && process.ProcessId.HasValue
            && process.IdentityVerified)
            return PalServerRuntimeState.Running;

        return PalServerRuntimeState.Faulted;
    }

    private static void ValidateFinalState(string operationType, PalServerProcessSnapshot process)
    {
        if (operationType is "start" or "restart")
        {
            if (!process.ProcessId.HasValue || !process.IdentityVerified)
                throw new PalServerRuntimeException(500, "PALSERVER_START_FAILED", "操作结束后未找到身份已验证的 PalServer 进程。", process.IdentityReason);
            return;
        }
        if (process.ProcessId.HasValue)
            throw new PalServerRuntimeException(500, "PALSERVER_STOP_FAILED", "操作结束后 PalServer 进程仍在运行。", process.ProcessId.Value.ToString());
    }

    private void SetOperation(
        ServerOperationSnapshot seed,
        string state,
        string stage,
        int progress,
        string message,
        PalServerRuntimeState runtimeState)
    {
        var operation = seed with
        {
            State = state,
            Stage = stage,
            Progress = Math.Clamp(progress, 0, 100),
            Message = message,
            CompletedAt = null,
            ErrorCode = null
        };
        _operations[operation.OperationId] = operation;
        lock (_sync)
        {
            _current = _current with
            {
                State = runtimeState.ToString(),
                ActiveOperation = operation,
                LastErrorCode = null,
                LastMessage = message,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
        var transition = stage switch
        {
            "starting-process" => "server.starting",
            "sending-shutdown" => "server.stopping",
            _ => null
        };
        if (transition is not null) PublishRuntimeEvent(transition, "information", operation.OperationId, operation.Type, null);
    }

    private void CompleteOperation(
        ServerOperationSnapshot seed,
        string state,
        string stage,
        string message,
        PalServerRuntimeState runtimeState,
        string? errorCode,
        PalServerProcessSnapshot process,
        HostMetricsSnapshot? metric)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var operation = seed with
        {
            State = state,
            Stage = stage,
            Progress = 100,
            Message = message,
            CompletedAt = completedAt,
            ErrorCode = errorCode
        };
        _operations[operation.OperationId] = operation;
        lock (_sync)
        {
            _current = _current with
            {
                State = runtimeState.ToString(),
                Process = process,
                Metrics = metric,
                ActiveOperation = null,
                LastErrorCode = errorCode,
                LastMessage = message,
                CapturedAt = completedAt
            };
        }
        var eventType = state == "completed"
            ? seed.Type switch
            {
                "start" => "server.started",
                "stop" => "server.stopped",
                "restart" => "server.restarted",
                "force-stop" => "server.force-stopped",
                _ => "server.operation.completed"
            }
            : errorCode == "PALSERVER_FORCE_STOP_TIMEOUT"
                ? "server.stop-timeout"
                : "server.operation.failed";
        var severity = eventType switch
        {
            "server.force-stopped" => "critical",
            "server.stop-timeout" => "warning",
            "server.operation.failed" => "error",
            _ => "information"
        };
        PublishRuntimeEvent(eventType, severity, operation.OperationId, operation.Type, errorCode);
    }

    private void PublishRuntimeEvent(
        string eventType,
        string severity,
        string? operationId,
        string? operationType,
        string? errorCode,
        int? processIdOverride = null,
        string? executablePathOverride = null)
    {
        var snapshot = Current;
        _ = PublishRuntimeEventBestEffortAsync(PalOpsEvent.Create(
            eventType,
            severity,
            server: new Dictionary<string, object?>
            {
                ["state"] = snapshot.State,
                ["processId"] = processIdOverride ?? snapshot.Process.ProcessId,
                ["executablePath"] = executablePathOverride ?? snapshot.Process.ExecutablePath,
                ["operationId"] = operationId
            },
            metadata: new Dictionary<string, object?>
            {
                ["operationType"] = operationType,
                ["errorCode"] = errorCode
            }));
    }

    private async Task PublishRuntimeEventBestEffortAsync(PalOpsEvent palOpsEvent)
    {
        try
        {
            await eventPublisher.PublishAsync(palOpsEvent).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to publish runtime event {EventType}.", palOpsEvent.EventType);
        }
    }

    private static PalServerRuntimeState InitialState(string type) => type switch
    {
        "start" => PalServerRuntimeState.Starting,
        "restart" => PalServerRuntimeState.Restarting,
        _ => PalServerRuntimeState.Stopping
    };

    private static PalServerRuntimeSnapshot Empty() => new(
        PalServerRuntimeState.Stopped.ToString(),
        new(null, PalServerRuntimeState.Stopped.ToString(), true, "尚未检测。", null, null, null, 0, null),
        null,
        null,
        null,
        false,
        false,
        null,
        null,
        DateTimeOffset.UtcNow);
}
