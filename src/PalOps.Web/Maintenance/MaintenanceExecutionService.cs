using System.Collections.Concurrent;
using PalOps.Web.Audit;
using PalOps.Web.Automation;
using PalOps.Web.Backups;
using PalOps.Web.Events;
using PalOps.Web.External;
using PalOps.Web.Rcon;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Settings;
using PalOps.Web.Platform.Tasks;

namespace PalOps.Web.Maintenance;

public interface IMaintenanceExecutionService
{
    bool IsBusy { get; }
    IReadOnlySet<string> RunningPlanIds { get; }
    Task<MaintenanceDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceRun>> ListRunsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<MaintenanceRun?> FindRunAsync(string id, CancellationToken cancellationToken = default);
    Task RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default);
    Task<MaintenanceRun> StartAsync(
        string planId,
        string trigger,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(string runId, CancellationToken cancellationToken = default);
}

public sealed class MaintenanceExecutionService(
    IMaintenanceRepository repository,
    IPalServerRuntimeCoordinator runtime,
    IServerOperationWaiter operationWaiter,
    IServerSettingsStore settingsStore,
    IRconClient rcon,
    IPalDefenderApiClient palDefender,
    IPalworldApiClient palworld,
    IBackupService backups,
    IMaintenanceScriptRunner scriptRunner,
    CrashGuardEvaluator crashGuardEvaluator,
    IAuditLogService audit,
    IPalOpsEventPublisher eventPublisher,
    IPlatformTaskCoordinator taskCenter,
    IMaintenanceActivityGate activityGate,
    ILogger<MaintenanceExecutionService> logger) : IMaintenanceExecutionService
{
    private readonly ConcurrentDictionary<string, ActiveRun> _activeRuns = new(StringComparer.OrdinalIgnoreCase);

    public bool IsBusy => activityGate.IsBusy;
    public IReadOnlySet<string> RunningPlanIds => _activeRuns.Values
        .Select(item => item.PlanId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<MaintenanceDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await repository.GetCrashGuardConfigurationAsync(cancellationToken);
        var state = await repository.GetCrashGuardStateAsync(cancellationToken);
        var crashEvents = await repository.ListCrashEventsAsync(100, cancellationToken);
        var evaluation = crashGuardEvaluator.Evaluate(configuration, state, crashEvents, DateTimeOffset.UtcNow);
        var crashStatus = new CrashGuardStatus(
            evaluation.Status,
            configuration,
            state,
            evaluation.CrashesInWindow,
            evaluation.WindowStartedAt,
            evaluation.NextEligibleRestartAt,
            crashEvents.Take(50).ToArray());
        var plans = await repository.ListPlansAsync(cancellationToken);
        var runs = await repository.ListRunsAsync(100, cancellationToken);
        var activeId = _activeRuns.Keys.FirstOrDefault();
        var activeRun = activeId is null ? null : await repository.FindRunAsync(activeId, cancellationToken);
        return new MaintenanceDashboard(crashStatus, activeRun, plans, runs, IsBusy);
    }

    public Task<IReadOnlyList<MaintenanceRun>> ListRunsAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        repository.ListRunsAsync(limit, cancellationToken);

    public Task<MaintenanceRun?> FindRunAsync(string id, CancellationToken cancellationToken = default) =>
        repository.FindRunAsync(id, cancellationToken);

    public async Task<MaintenanceRun> StartAsync(
        string planId,
        string trigger,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activityLease = activityGate.TryAcquire("maintenance-plan:" + planId);
        if (activityLease is null)
            throw new InvalidOperationException("已有维护流程或崩溃恢复正在执行。");

        MaintenancePlan? plan = null;
        MaintenanceRun? run = null;
        try
        {
            plan = await repository.FindPlanAsync(planId, cancellationToken)
                ?? throw new KeyNotFoundException("维护计划不存在。");
            var now = DateTimeOffset.UtcNow;
            run = new MaintenanceRun(
                Guid.NewGuid().ToString("N"),
                plan.Id,
                plan.Name,
                NormalizeTrigger(trigger),
                MaintenanceStatuses.Queued,
                "queued",
                0,
                now,
                null,
                Limit(userName, 100),
                Limit(remoteIp, 100),
                "维护流程已排队。",
                null,
                BuildSteps(plan));
            await repository.UpsertRunAsync(run, cancellationToken);

            var nextRun = plan.Enabled
                ? AutomationSchedule.GetNextRun(plan.ScheduleType, plan.ScheduleExpression, now)
                : null;
            var planEnabled = plan.Enabled && !(plan.ScheduleType == "once" && nextRun is null);
            plan = plan with
            {
                Enabled = planEnabled,
                LastRunAt = now,
                NextRunAt = nextRun,
                LastStatus = MaintenanceStatuses.Queued,
                LastMessage = "维护流程已排队。",
                UpdatedAt = now
            };
            await repository.UpsertPlanAsync(plan, cancellationToken);

            var scheduledPlan = plan ?? throw new InvalidOperationException("维护计划初始化失败。");
            var scheduledRun = run ?? throw new InvalidOperationException("维护运行记录初始化失败。");
            var platformTaskId = "maintenance-" + scheduledRun.Id;
            if (!_activeRuns.TryAdd(scheduledRun.Id, new ActiveRun(scheduledPlan.Id, platformTaskId, activityLease)))
                throw new InvalidOperationException("无法登记维护流程运行状态。");

            await taskCenter.EnqueueAsync(
                new PlatformTaskSubmission(
                    "maintenance",
                    "执行维护计划：" + scheduledPlan.Name,
                    scheduledRun.StartedBy,
                    scheduledRun.RemoteIp,
                    ResourceKey: "server-maintenance",
                    Priority: 50,
                    TimeoutSeconds: Math.Max(600, scheduledPlan.ScriptTimeoutSeconds + scheduledPlan.HealthTimeoutSeconds + scheduledPlan.AnnouncementCountdownSeconds + 600),
                    MaximumAttempts: 1,
                    CorrelationId: scheduledRun.Id,
                    Metadata: new Dictionary<string, string>
                    {
                        ["runId"] = scheduledRun.Id,
                        ["planId"] = scheduledPlan.Id,
                        ["trigger"] = scheduledRun.Trigger
                    },
                    TaskId: platformTaskId),
                async (context, token) =>
                {
                    await context.ReportProgressAsync(1, "starting", "维护流程开始执行。", token);
                    await ExecuteAsync(scheduledPlan, scheduledRun, token);
                    await context.ReportProgressAsync(100, "completed", "维护流程已完成。", CancellationToken.None);
                },
                cancellationToken);
            return scheduledRun;
        }
        catch (Exception ex)
        {
            if (run is not null && _activeRuns.TryRemove(run.Id, out var active)) active.ActivityLease.Dispose();
            else activityLease.Dispose();
            if (run is not null)
                await MarkStartFailureAsync(plan, run, ex, CancellationToken.None);
            throw;
        }
    }

    public async Task RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default)
    {
        var interruptedRuns = (await repository.ListRunsAsync(500, cancellationToken))
            .Where(run => !run.IsTerminal)
            .ToArray();
        foreach (var run in interruptedRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recovered = MarkInterruptedSteps(run) with
            {
                Status = MaintenanceStatuses.Failed,
                CurrentStage = "interrupted",
                CompletedAt = DateTimeOffset.UtcNow,
                Message = "PalOps 重启时发现未完成的维护流程，已停止续跑；请核对服务器状态后人工处理。",
                ErrorCode = "MAINTENANCE_INTERRUPTED_BY_RESTART"
            };
            await repository.UpsertRunAsync(recovered, cancellationToken);
            var plan = await repository.FindPlanAsync(recovered.PlanId, cancellationToken);
            if (plan is not null)
            {
                await repository.UpsertPlanAsync(plan with
                {
                    LastRunAt = recovered.StartedAt,
                    LastStatus = recovered.Status,
                    LastMessage = recovered.Message,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
            await PublishAsync("maintenance.interrupted", "error", recovered, recovered.ErrorCode, cancellationToken);
            await AuditAsync(recovered, "failed", cancellationToken);
        }
    }

    public async Task<bool> CancelAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_activeRuns.TryGetValue((runId ?? string.Empty).Trim(), out var active)) return false;
        return await taskCenter.CancelAsync(active.TaskId, cancellationToken);
    }


    private async Task MarkStartFailureAsync(
        MaintenancePlan? plan,
        MaintenanceRun run,
        Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            var failed = MarkPendingSteps(run, MaintenanceStatuses.Skipped, "维护流程未能启动，步骤未执行。") with
            {
                Status = MaintenanceStatuses.Failed,
                CurrentStage = "start-failed",
                CompletedAt = DateTimeOffset.UtcNow,
                Message = "维护流程启动失败：" + Limit(error.Message, 400),
                ErrorCode = "MAINTENANCE_START_FAILED"
            };
            await repository.UpsertRunAsync(failed, cancellationToken);
            if (plan is not null) await UpdatePlanOutcomeAsync(plan, failed, cancellationToken);
            await PublishAsync("maintenance.failed", "error", failed, failed.ErrorCode, cancellationToken);
            await AuditAsync(failed, "failed", cancellationToken);
        }
        catch (Exception persistError)
        {
            logger.LogError(persistError, "Failed to persist maintenance start failure for run {RunId}.", run.Id);
        }
    }

    private async Task ExecuteAsync(MaintenancePlan plan, MaintenanceRun seed, CancellationToken cancellationToken)
    {
        var context = new RunContext(seed with
        {
            Status = MaintenanceStatuses.Running,
            CurrentStage = "starting",
            Message = "维护流程开始执行。"
        });
        try
        {
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await PublishAsync("maintenance.started", "information", context.Run, null, CancellationToken.None);

            await ExecuteStepAsync(context, MaintenanceStepKeys.Announcement,
                token => ExecuteAnnouncementAsync(plan, token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.SaveWorld,
                token => ExecuteSaveWorldAsync(token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.Backup,
                token => ExecuteBackupAsync(plan, context.Run.Id, token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.StopServer,
                token => ExecuteStopAsync(context.Run, plan, token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.Script,
                token => ExecuteScriptAsync(plan, token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.StartServer,
                token => ExecuteStartAsync(context.Run, plan, token), cancellationToken);
            await ExecuteStepAsync(context, MaintenanceStepKeys.HealthVerification,
                token => ExecuteHealthVerificationAsync(plan, token), cancellationToken);

            context.Run = context.Run with
            {
                Status = MaintenanceStatuses.Succeeded,
                CurrentStage = "completed",
                Progress = 100,
                CompletedAt = DateTimeOffset.UtcNow,
                Message = "维护流程已完成。",
                ErrorCode = null
            };
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await UpdatePlanOutcomeAsync(plan, context.Run, CancellationToken.None);
            await PublishAsync("maintenance.completed", "information", context.Run, null, CancellationToken.None);
            await AuditAsync(context.Run, "success", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            context.Run = MarkPendingSteps(context.Run, MaintenanceStatuses.Cancelled, "维护流程已取消。") with
            {
                Status = MaintenanceStatuses.Cancelled,
                CurrentStage = "cancelled",
                CompletedAt = DateTimeOffset.UtcNow,
                Message = "维护流程已取消。",
                ErrorCode = "MAINTENANCE_CANCELLED"
            };
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await UpdatePlanOutcomeAsync(plan, context.Run, CancellationToken.None);
            await PublishAsync("maintenance.cancelled", "warning", context.Run, "MAINTENANCE_CANCELLED", CancellationToken.None);
            await AuditAsync(context.Run, "cancelled", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var errorCode = ex is TimeoutException ? "MAINTENANCE_TIMEOUT" : "MAINTENANCE_STEP_FAILED";
            context.Run = MarkPendingSteps(context.Run, MaintenanceStatuses.Skipped, "前置步骤失败，未执行。") with
            {
                Status = MaintenanceStatuses.Failed,
                CurrentStage = "failed",
                CompletedAt = DateTimeOffset.UtcNow,
                Message = Limit(ex.Message, 500),
                ErrorCode = errorCode
            };
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await UpdatePlanOutcomeAsync(plan, context.Run, CancellationToken.None);
            await PublishAsync("maintenance.failed", "error", context.Run, errorCode, CancellationToken.None);
            await AuditAsync(context.Run, "failed", CancellationToken.None);
            logger.LogError(ex, "Maintenance run {RunId} failed at {Stage}.", context.Run.Id, context.Run.CurrentStage);
            throw;
        }
        finally
        {
            if (_activeRuns.TryRemove(seed.Id, out var active))
            {
                active.ActivityLease.Dispose();
            }
        }
    }

    private async Task ExecuteStepAsync(
        RunContext context,
        string key,
        Func<CancellationToken, Task<string>> action,
        CancellationToken cancellationToken)
    {
        var step = context.Run.Steps.First(item => item.Key == key);
        if (step.Status == MaintenanceStatuses.Skipped) return;

        var startedAt = DateTimeOffset.UtcNow;
        var runningStep = step with
        {
            Status = MaintenanceStatuses.Running,
            StartedAt = startedAt,
            Message = "正在执行。"
        };
        context.Run = ReplaceStep(context.Run, runningStep) with
        {
            CurrentStage = key,
            Progress = runningStep.ProgressStart,
            Message = runningStep.Name + "正在执行。"
        };
        await repository.UpsertRunAsync(context.Run, CancellationToken.None);
        await PublishAsync("maintenance.step.started", "information", context.Run, null, CancellationToken.None);

        try
        {
            var message = await action(cancellationToken);
            var completedStep = runningStep with
            {
                Status = MaintenanceStatuses.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                Message = Limit(message, 500),
                Output = Limit(message, 4000)
            };
            context.Run = ReplaceStep(context.Run, completedStep) with
            {
                Progress = completedStep.ProgressEnd,
                Message = completedStep.Message
            };
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await PublishAsync("maintenance.step.completed", "information", context.Run, null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var cancelledStep = runningStep with
            {
                Status = MaintenanceStatuses.Cancelled,
                CompletedAt = DateTimeOffset.UtcNow,
                Message = "步骤已取消。"
            };
            context.Run = ReplaceStep(context.Run, cancelledStep);
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var failedStep = runningStep with
            {
                Status = MaintenanceStatuses.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Message = Limit(ex.Message, 500),
                Output = Limit(ex.ToString(), 4000)
            };
            context.Run = ReplaceStep(context.Run, failedStep) with
            {
                CurrentStage = key,
                Message = failedStep.Message
            };
            await repository.UpsertRunAsync(context.Run, CancellationToken.None);
            await PublishAsync("maintenance.step.failed", "error", context.Run, "MAINTENANCE_STEP_FAILED", CancellationToken.None);
            throw;
        }
    }

    private async Task<string> ExecuteAnnouncementAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        var checkpoints = new[] { plan.AnnouncementCountdownSeconds, 300, 120, 60, 30, 10, 0 }
            .Where(value => value <= plan.AnnouncementCountdownSeconds)
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
        for (var index = 0; index < checkpoints.Length; index++)
        {
            var seconds = checkpoints[index];
            var message = plan.AnnouncementMessage.Replace("{seconds}", seconds.ToString(), StringComparison.OrdinalIgnoreCase);
            await palDefender.BroadcastAsync(message, seconds <= 30, cancellationToken);
            if (index + 1 < checkpoints.Length)
                await Task.Delay(TimeSpan.FromSeconds(seconds - checkpoints[index + 1]), cancellationToken);
        }
        return $"维护公告倒计时 {plan.AnnouncementCountdownSeconds} 秒已完成。";
    }

    private async Task<string> ExecuteSaveWorldAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var result = await rcon.ExecuteAsync(settings.Rcon, "Save", cancellationToken);
        EnsureRconSuccess(result.Response);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return $"世界存档保存完成，RCON 延迟 {result.ElapsedMilliseconds}ms。";
    }

    private async Task<string> ExecuteBackupAsync(MaintenancePlan plan, string runId, CancellationToken cancellationToken)
    {
        var note = string.IsNullOrWhiteSpace(plan.BackupNote)
            ? $"维护流程 {runId}"
            : $"{plan.BackupNote}（维护流程 {runId}）";
        var result = await backups.CreateAsync(note, false, cancellationToken);
        return $"备份已创建：{result.FileName}，{result.FileCount} 个文件。";
    }

    private async Task<string> ExecuteStopAsync(MaintenanceRun run, MaintenancePlan plan, CancellationToken cancellationToken)
    {
        var snapshot = await runtime.RefreshAsync(true, cancellationToken);
        if (!snapshot.Process.ProcessId.HasValue) return "PalServer 已处于停止状态。";
        var operation = await runtime.StopAsync(run.StartedBy, run.RemoteIp, cancellationToken);
        var completed = await operationWaiter.WaitAsync(operation.OperationId, Math.Max(plan.HealthTimeoutSeconds, 180), cancellationToken);
        if (completed.State != "completed")
            throw new InvalidOperationException(completed.Message ?? "PalServer 安全停服失败。");
        return completed.Message ?? "PalServer 安全停服完成。";
    }

    private async Task<string> ExecuteScriptAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        var result = await scriptRunner.RunAsync(plan.ScriptPath, plan.ScriptArguments, plan.ScriptTimeoutSeconds, cancellationToken);
        var output = string.IsNullOrWhiteSpace(result.StandardOutput) ? "无标准输出" : result.StandardOutput;
        return $"维护脚本执行成功，退出代码 {result.ExitCode}，耗时 {result.DurationMs}ms。\n{Limit(output, 3000)}";
    }

    private async Task<string> ExecuteStartAsync(MaintenanceRun run, MaintenancePlan plan, CancellationToken cancellationToken)
    {
        var snapshot = await runtime.RefreshAsync(true, cancellationToken);
        if (snapshot.Process.ProcessId.HasValue && snapshot.Process.IdentityVerified)
            return $"PalServer 已运行，进程 ID {snapshot.Process.ProcessId}。";
        var operation = await runtime.StartAsync(run.StartedBy, run.RemoteIp, cancellationToken);
        var completed = await operationWaiter.WaitAsync(operation.OperationId, Math.Max(plan.HealthTimeoutSeconds, 180), cancellationToken);
        if (completed.State != "completed")
            throw new InvalidOperationException(completed.Message ?? "PalServer 启动失败。");
        return completed.Message ?? "PalServer 启动完成。";
    }

    private async Task<string> ExecuteHealthVerificationAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(plan.HealthTimeoutSeconds);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var messages = new List<string>();
                if (plan.VerifyProcess)
                {
                    var snapshot = await runtime.RefreshAsync(true, cancellationToken);
                    if (!snapshot.Process.ProcessId.HasValue || !snapshot.Process.IdentityVerified)
                        throw new InvalidOperationException(snapshot.Process.IdentityReason);
                    messages.Add($"进程 {snapshot.Process.ProcessId} 已验证");
                }
                if (plan.VerifyRest)
                {
                    var response = await palworld.GetInfoAsync(cancellationToken);
                    messages.Add($"REST 正常（{response.Length} 字符）");
                }
                if (plan.VerifyRcon)
                {
                    var settings = await settingsStore.GetAsync(cancellationToken);
                    var result = await rcon.ExecuteAsync(settings.Rcon, "Info", cancellationToken);
                    EnsureRconSuccess(result.Response);
                    messages.Add($"RCON 正常（{result.ElapsedMilliseconds}ms）");
                }
                return "健康验证通过：" + string.Join("；", messages) + "。";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(plan.HealthRetrySeconds), cancellationToken);
            }
        }
        throw new TimeoutException("维护后健康验证失败：" + Limit(lastError?.Message ?? "未达到健康状态。", 400));
    }

    private async Task UpdatePlanOutcomeAsync(MaintenancePlan original, MaintenanceRun run, CancellationToken cancellationToken)
    {
        var current = await repository.FindPlanAsync(original.Id, cancellationToken);
        if (current is null) return;
        await repository.UpsertPlanAsync(current with
        {
            LastRunAt = run.StartedAt,
            LastStatus = run.Status,
            LastMessage = run.Message,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private async Task PublishAsync(
        string eventType,
        string severity,
        MaintenanceRun run,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await eventPublisher.PublishAsync(PalOpsEvent.Create(
                eventType,
                severity,
                server: new Dictionary<string, object?>
                {
                    ["operationId"] = run.Id,
                    ["state"] = runtime.Current.State,
                    ["processId"] = runtime.Current.Process.ProcessId
                },
                metadata: new Dictionary<string, object?>
                {
                    ["message"] = run.Message,
                    ["planId"] = run.PlanId,
                    ["planName"] = run.PlanName,
                    ["runId"] = run.Id,
                    ["trigger"] = run.Trigger,
                    ["status"] = run.Status,
                    ["stage"] = run.CurrentStage,
                    ["progress"] = run.Progress,
                    ["errorCode"] = errorCode
                }), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish maintenance event {EventType}.", eventType);
        }
    }

    private async Task AuditAsync(MaintenanceRun run, string outcome, CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(
                "maintenance.run",
                outcome,
                run.RemoteIp,
                $"维护计划 {run.PlanName}：{run.Message}",
                new { run.Id, run.PlanId, run.Trigger, run.Status, run.CurrentStage, run.ErrorCode },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write maintenance audit record for {RunId}.", run.Id);
        }
    }

    private static IReadOnlyList<MaintenanceRunStep> BuildSteps(MaintenancePlan plan) =>
    [
        Step(MaintenanceStepKeys.Announcement, "维护公告倒计时", 0, 12, plan.AnnouncementEnabled),
        Step(MaintenanceStepKeys.SaveWorld, "保存世界存档", 12, 24, plan.SaveWorld),
        Step(MaintenanceStepKeys.Backup, "创建维护备份", 24, 42, plan.CreateBackup),
        Step(MaintenanceStepKeys.StopServer, "安全停止服务器", 42, 58, plan.StopServer),
        Step(MaintenanceStepKeys.Script, "执行维护脚本", 58, 72, plan.ScriptEnabled),
        Step(MaintenanceStepKeys.StartServer, "启动服务器", 72, 86, plan.StartServer),
        Step(MaintenanceStepKeys.HealthVerification, "REST / RCON 健康验证", 86, 100,
            plan.StartServer && (plan.VerifyProcess || plan.VerifyRest || plan.VerifyRcon))
    ];

    private static MaintenanceRunStep Step(string key, string name, int start, int end, bool enabled) => new(
        key,
        name,
        enabled ? MaintenanceStatuses.Pending : MaintenanceStatuses.Skipped,
        start,
        end,
        null,
        null,
        enabled ? "等待执行。" : "计划未启用此步骤。",
        string.Empty);

    private static MaintenanceRun ReplaceStep(MaintenanceRun run, MaintenanceRunStep replacement)
    {
        var steps = run.Steps.Select(item => item.Key == replacement.Key ? replacement : item).ToArray();
        return run with { Steps = steps };
    }

    private static MaintenanceRun MarkPendingSteps(MaintenanceRun run, string status, string message)
    {
        var now = DateTimeOffset.UtcNow;
        var steps = run.Steps.Select(item => item.Status == MaintenanceStatuses.Pending
            ? item with { Status = status, CompletedAt = now, Message = message }
            : item).ToArray();
        return run with { Steps = steps };
    }

    private static MaintenanceRun MarkInterruptedSteps(MaintenanceRun run)
    {
        var now = DateTimeOffset.UtcNow;
        var steps = run.Steps.Select(item => item.Status switch
        {
            MaintenanceStatuses.Running => item with
            {
                Status = MaintenanceStatuses.Failed,
                CompletedAt = now,
                Message = "PalOps 进程中断，步骤结果未知。"
            },
            MaintenanceStatuses.Pending => item with
            {
                Status = MaintenanceStatuses.Skipped,
                CompletedAt = now,
                Message = "PalOps 进程中断，步骤未执行。"
            },
            _ => item
        }).ToArray();
        return run with { Steps = steps };
    }

    private static void EnsureRconSuccess(string response)
    {
        var value = response ?? string.Empty;
        if (value.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("invalid argument", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RCON 返回失败：" + Limit(value, 400));
    }

    private static string NormalizeTrigger(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        return value is "manual" or "scheduler" ? value : "system";
    }

    private static string Limit(string? value, int maximum)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private sealed class RunContext(MaintenanceRun run)
    {
        public MaintenanceRun Run { get; set; } = run;
    }

    private sealed record ActiveRun(string PlanId, string TaskId, IDisposable ActivityLease);
}
