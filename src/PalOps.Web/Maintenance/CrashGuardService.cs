using PalOps.Web.Audit;
using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Maintenance;

public sealed class CrashGuardService(
    IMaintenanceRepository repository,
    CrashGuardEvaluator evaluator,
    IPalServerRuntimeCoordinator runtime,
    IServerOperationWaiter operationWaiter,
    IMaintenanceActivityGate activityGate,
    IPalOpsEventBus eventBus,
    IAuditLogService audit,
    IBackgroundWorkerSupervisor workerSupervisor,
    ILogger<CrashGuardService> logger,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("crash-guard", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await using var subscription = eventBus.Subscribe("crash-guard", 200);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = RunHeartbeatAsync(linkedCts.Token);

        try
        {
            await foreach (var palOpsEvent in subscription.ReadAllAsync(linkedCts.Token))
            {
                workerSupervisor.Heartbeat("crash-guard");
                if (!palOpsEvent.EventType.Equals("server.exited-unexpectedly", StringComparison.OrdinalIgnoreCase)) continue;
                var operational = await readinessGate.GetSnapshotAsync(linkedCts.Token).ConfigureAwait(false);
                if (!operational.HasAnyOperationalConfiguration) continue;
                try
                {
                    await HandleUnexpectedExitAsync(palOpsEvent, linkedCts.Token);
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Crash guard failed to handle unexpected server exit event {EventId}.", palOpsEvent.EventId);
                }
            }
        }
        finally
        {
            linkedCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        workerSupervisor.Heartbeat("crash-guard");
        while (await timer.WaitForNextTickAsync(cancellationToken))
            workerSupervisor.Heartbeat("crash-guard");
    }

    private async Task HandleUnexpectedExitAsync(PalOpsEvent source, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var processId = ReadProcessId(source);
        await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
            Guid.NewGuid().ToString("N"),
            "unexpected-exit",
            now,
            processId,
            "detected",
            "检测到 PalServer 意外退出。",
            source.Server.TryGetValue("operationId", out var operationId) ? operationId?.ToString() : null,
            "PALSERVER_EXITED_UNEXPECTEDLY"), cancellationToken);

        var configuration = await repository.GetCrashGuardConfigurationAsync(cancellationToken);
        var state = await repository.GetCrashGuardStateAsync(cancellationToken);
        state = state with
        {
            LastCrashAt = now,
            LastMessage = "检测到 PalServer 意外退出。"
        };
        await repository.SaveCrashGuardStateAsync(state, cancellationToken);

        if (!configuration.Enabled)
        {
            await RecordSuppressedAsync(processId, "崩溃守护未启用，未执行自动重启。", "disabled", cancellationToken);
            return;
        }
        if (state.Suspended)
        {
            await RecordSuppressedAsync(processId, "崩溃守护已人工暂停，未执行自动重启。", "suspended", cancellationToken);
            return;
        }
        if (state.CircuitOpen)
        {
            await RecordSuppressedAsync(processId, "崩溃守护熔断器处于开启状态，未执行自动重启。", "circuit-open", cancellationToken);
            return;
        }

        var events = await repository.ListCrashEventsAsync(1000, cancellationToken);
        var evaluation = evaluator.Evaluate(configuration, state, events, now);
        if (evaluation.ThresholdReached)
        {
            await OpenCircuitAsync(
                state,
                $"{configuration.WindowMinutes} 分钟内检测到 {evaluation.CrashesInWindow} 次崩溃，已触发熔断。",
                processId,
                "CRASH_GUARD_THRESHOLD_REACHED",
                cancellationToken);
            return;
        }

        if (configuration.RestartDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(configuration.RestartDelaySeconds), cancellationToken);

        using var activityLease = activityGate.TryAcquire("crash-guard");
        if (activityLease is null)
        {
            await RecordSuppressedAsync(processId, "维护流程正在执行，崩溃守护放弃本次自动重启。", "maintenance-busy", cancellationToken);
            return;
        }

        configuration = await repository.GetCrashGuardConfigurationAsync(cancellationToken);
        state = await repository.GetCrashGuardStateAsync(cancellationToken);
        if (!configuration.Enabled || state.Suspended || state.CircuitOpen)
        {
            await RecordSuppressedAsync(processId, "崩溃守护状态已变化，放弃本次自动重启。", "state-changed", cancellationToken);
            return;
        }

        var current = await runtime.RefreshAsync(true, cancellationToken);
        if (current.Process.ProcessId.HasValue || current.ActiveOperation is not null)
        {
            await RecordSuppressedAsync(processId, "服务器已恢复或存在运行中的服务器操作，无需自动重启。", "already-recovered", cancellationToken);
            return;
        }

        var restartStartedAt = DateTimeOffset.UtcNow;
        await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
            Guid.NewGuid().ToString("N"),
            "auto-restart-started",
            restartStartedAt,
            processId,
            "running",
            "崩溃守护正在启动 PalServer。",
            null,
            null), cancellationToken);
        if (configuration.NotifyOnRestart)
            await PublishAsync("maintenance.crash-guard.restarting", "warning", "崩溃守护正在自动启动 PalServer。", null, cancellationToken);

        try
        {
            var operation = await runtime.StartAsync("crash-guard", "local", cancellationToken);
            var completed = await operationWaiter.WaitAsync(operation.OperationId, configuration.OperationTimeoutSeconds, cancellationToken);
            if (completed.State != "completed")
                throw new InvalidOperationException(completed.Message ?? "自动重启失败。");

            var successAt = DateTimeOffset.UtcNow;
            state = state with
            {
                LastRestartAt = successAt,
                LastRestartOutcome = "success",
                LastMessage = "崩溃守护自动重启成功。"
            };
            await repository.SaveCrashGuardStateAsync(state, cancellationToken);
            await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
                Guid.NewGuid().ToString("N"),
                "auto-restart-succeeded",
                successAt,
                runtime.Current.Process.ProcessId,
                "success",
                state.LastMessage,
                operation.OperationId,
                null), cancellationToken);
            if (configuration.NotifyOnRestart)
                await PublishAsync("maintenance.crash-guard.recovered", "information", state.LastMessage, null, cancellationToken);
            await WriteAuditAsync("success", state.LastMessage, new { operation.OperationId, processId }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var message = "崩溃守护自动重启失败：" + Limit(ex.Message, 400);
            state = state with
            {
                CircuitOpen = true,
                CircuitOpenedAt = failedAt,
                LastRestartAt = failedAt,
                LastRestartOutcome = "failed",
                LastMessage = message
            };
            await repository.SaveCrashGuardStateAsync(state, CancellationToken.None);
            await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
                Guid.NewGuid().ToString("N"),
                "auto-restart-failed",
                failedAt,
                processId,
                "failed",
                message,
                null,
                "CRASH_GUARD_RESTART_FAILED"), CancellationToken.None);
            await PublishAsync("maintenance.crash-guard.failed", "error", message, "CRASH_GUARD_RESTART_FAILED", CancellationToken.None);
            await WriteAuditAsync("failed", message, new { processId, error = ex.GetType().Name }, CancellationToken.None);
            logger.LogError(ex, "Crash guard automatic restart failed; circuit has been opened.");
        }
    }

    private async Task OpenCircuitAsync(
        CrashGuardState state,
        string message,
        int? processId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        state = state with
        {
            CircuitOpen = true,
            CircuitOpenedAt = now,
            LastRestartOutcome = "circuit-open",
            LastMessage = message
        };
        await repository.SaveCrashGuardStateAsync(state, cancellationToken);
        await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
            Guid.NewGuid().ToString("N"),
            "circuit-opened",
            now,
            processId,
            "blocked",
            message,
            null,
            errorCode), cancellationToken);
        await PublishAsync("maintenance.crash-guard.circuit-opened", "critical", message, errorCode, cancellationToken);
        await WriteAuditAsync("failed", message, new { processId, errorCode }, cancellationToken);
    }

    private async Task RecordSuppressedAsync(
        int? processId,
        string message,
        string outcome,
        CancellationToken cancellationToken)
    {
        await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
            Guid.NewGuid().ToString("N"),
            "auto-restart-suppressed",
            DateTimeOffset.UtcNow,
            processId,
            outcome,
            message,
            null,
            null), cancellationToken);
    }

    private async Task PublishAsync(
        string eventType,
        string severity,
        string message,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.PublishAsync(PalOpsEvent.Create(
                eventType,
                severity,
                server: new Dictionary<string, object?>
                {
                    ["state"] = runtime.Current.State,
                    ["processId"] = runtime.Current.Process.ProcessId
                },
                metadata: new Dictionary<string, object?>
                {
                    ["message"] = message,
                    ["errorCode"] = errorCode
                }), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish crash guard event {EventType}.", eventType);
        }
    }

    private async Task WriteAuditAsync(string outcome, string message, object data, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync("maintenance.crash-guard", outcome, "local", message, data, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write crash guard audit record.");
        }
    }

    private static int? ReadProcessId(PalOpsEvent source)
    {
        if (!source.Server.TryGetValue("processId", out var value) || value is null) return null;
        if (value is int intValue) return intValue;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string Limit(string value, int maximum)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
