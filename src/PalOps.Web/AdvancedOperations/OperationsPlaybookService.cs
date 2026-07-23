using PalOps.Web.Backups;
using PalOps.Web.Events;
using PalOps.Web.Health;
using PalOps.Web.Maintenance;
using PalOps.Web.SaveGames;

namespace PalOps.Web.AdvancedOperations;

public interface IOperationsPlaybookService
{
    Task<OperationsPlaybookDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<OperationsPlaybook> UpsertAsync(string? id, OperationsPlaybookWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationsPlaybookRun> RunAsync(string id, OperationsPlaybookRunRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default);
}

public sealed class OperationsPlaybookService(
    IAdvancedOperationsRepository repository,
    ISystemHealthService health,
    IBackupService backups,
    ISaveIndexingService saveIndexing,
    IPalOpsEventPublisher eventPublisher,
    IMaintenanceExecutionService maintenance,
    AdvancedOperationsValidator validator,
    ILogger<OperationsPlaybookService> logger) : IOperationsPlaybookService
{
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public async Task<OperationsPlaybookDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        return new(
            state.Playbooks.Count,
            state.Playbooks.Count(static item => item.Enabled),
            state.PlaybookRuns.Count(static item => item.Status == AdvancedOperationStatus.Succeeded),
            state.PlaybookRuns.Count(static item => item.Status == AdvancedOperationStatus.Failed),
            state.Playbooks.OrderByDescending(static item => item.Enabled).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            state.PlaybookRuns.OrderByDescending(static item => item.StartedAt).Take(100).ToArray(),
            OperationsPlaybookCatalog.AllowedActions,
            DateTimeOffset.UtcNow);
    }

    public Task<OperationsPlaybook> UpsertAsync(string? id, OperationsPlaybookWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var description = validator.LimitText(request.Description, 1000);
        var steps = validator.ValidatePlaybookSteps(request.Steps);
        var now = DateTimeOffset.UtcNow;
        return repository.MutateAsync(state =>
        {
            var existing = string.IsNullOrWhiteSpace(id) ? null : state.Playbooks.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var playbook = new OperationsPlaybook(
                existing?.Id ?? Guid.NewGuid().ToString("N"), name, description, request.Enabled, steps,
                existing?.CreatedAt ?? now, now, actor, existing?.LastStatus ?? "never", existing?.LastMessage ?? "尚未执行。", existing?.LastRunAt);
            Replace(state.Playbooks, playbook);
            return playbook;
        }, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await repository.MutateAsync(state =>
        {
            var removed = state.Playbooks.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Operations playbook not found.");
            return true;
        }, cancellationToken);
    }

    public async Task<OperationsPlaybookRun> RunAsync(string id, OperationsPlaybookRunRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Another playbook is running.");
        try
        {
            var state = await repository.ReadAsync(cancellationToken);
            var playbook = state.Playbooks.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                           ?? throw new KeyNotFoundException("Operations playbook not found.");
            if (!playbook.Enabled) throw new InvalidOperationException("Operations playbook is disabled.");
            if (playbook.Steps.Any(static step => step.Action == "maintenance-run"))
                AdvancedOperationsValidator.RequireConfirmation(request.Confirmation, "RUN PLAYBOOK");

            var startedAt = DateTimeOffset.UtcNow;
            var runSteps = new List<OperationsPlaybookRunStep>();
            var failed = false;
            foreach (var step in playbook.Steps.OrderBy(static item => item.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepStarted = DateTimeOffset.UtcNow;
                var stepStatus = AdvancedOperationStatus.Succeeded;
                string message;
                try
                {
                    message = await ExecuteStepAsync(playbook, step, actor, remoteIp, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    stepStatus = AdvancedOperationStatus.Failed;
                    message = Limit(ex.Message);
                    failed = true;
                    logger.LogWarning(ex, "Playbook {PlaybookId} step {Action} failed.", playbook.Id, step.Action);
                }
                runSteps.Add(new(step.Order, step.Action, stepStatus, message, stepStarted, DateTimeOffset.UtcNow));
                if (stepStatus == AdvancedOperationStatus.Failed && !step.ContinueOnError) break;
            }

            var completedAt = DateTimeOffset.UtcNow;
            var status = failed ? AdvancedOperationStatus.Failed : AdvancedOperationStatus.Succeeded;
            var messageSummary = failed ? "一个或多个步骤失败。" : "所有步骤执行完成。";
            var run = new OperationsPlaybookRun(Guid.NewGuid().ToString("N"), playbook.Id, playbook.Name, status, messageSummary, startedAt, completedAt, actor, runSteps);
            await repository.MutateAsync(document =>
            {
                document.PlaybookRuns.Add(run);
                Replace(document.Playbooks, playbook with { LastStatus = status, LastMessage = messageSummary, LastRunAt = completedAt, UpdatedAt = completedAt });
                return true;
            }, cancellationToken);
            return run;
        }
        finally { _runGate.Release(); }
    }

    private async Task<string> ExecuteStepAsync(OperationsPlaybook playbook, OperationsPlaybookStep step, string actor, string remoteIp, CancellationToken cancellationToken)
    {
        return step.Action switch
        {
            "health-refresh" => await RefreshHealthAsync(cancellationToken),
            "backup-create" => await CreateBackupAsync(playbook, step, cancellationToken),
            "save-index" => await TriggerSaveIndexAsync(playbook, cancellationToken),
            "notification-event" => await PublishEventAsync(playbook, step, actor, cancellationToken),
            "maintenance-run" => await RunMaintenanceAsync(step, actor, remoteIp, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported playbook action.")
        };
    }

    private async Task<string> RefreshHealthAsync(CancellationToken cancellationToken)
    {
        await health.RefreshAsync(cancellationToken);
        var unavailable = health.Components.Count(static item => item.Status == "unavailable");
        return unavailable == 0 ? "健康检查完成。" : $"健康检查完成，{unavailable} 个组件不可用。";
    }

    private async Task<string> CreateBackupAsync(OperationsPlaybook playbook, OperationsPlaybookStep step, CancellationToken cancellationToken)
    {
        step.Parameters.TryGetValue("note", out var note);
        step.Parameters.TryGetValue("saveFirst", out var saveFirstText);
        var saveFirst = bool.TryParse(saveFirstText, out var parsed) && parsed;
        var backup = await backups.CreateAsync(string.IsNullOrWhiteSpace(note) ? $"Playbook: {playbook.Name}" : note, saveFirst, cancellationToken);
        return $"备份已创建：{backup.FileName}";
    }

    private async Task<string> TriggerSaveIndexAsync(OperationsPlaybook playbook, CancellationToken cancellationToken)
    {
        var result = await saveIndexing.TriggerAsync("playbook:" + playbook.Id, cancellationToken);
        return result.Message;
    }

    private async Task<string> PublishEventAsync(OperationsPlaybook playbook, OperationsPlaybookStep step, string actor, CancellationToken cancellationToken)
    {
        step.Parameters.TryGetValue("message", out var message);
        var eventType = "operations.playbook.notification";
        if (step.Parameters.TryGetValue("eventType", out var custom) && custom.StartsWith("operations.playbook.", StringComparison.OrdinalIgnoreCase))
            eventType = custom.ToLowerInvariant();
        await eventPublisher.PublishAsync(PalOpsEvent.Create(
            eventType,
            "information",
            metadata: new Dictionary<string, object?>
            {
                ["playbookId"] = playbook.Id,
                ["playbookName"] = playbook.Name,
                ["actor"] = actor,
                ["message"] = message ?? "Playbook notification"
            }), cancellationToken);
        return "通知事件已发布。";
    }

    private async Task<string> RunMaintenanceAsync(OperationsPlaybookStep step, string actor, string remoteIp, CancellationToken cancellationToken)
    {
        if (!step.Parameters.TryGetValue("planId", out var planId) || string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("maintenance-run requires parameter planId.");
        var run = await maintenance.StartAsync(planId, "playbook", actor, remoteIp, cancellationToken);
        return $"维护流程已启动：{run.Id}";
    }

    private static void Replace(List<OperationsPlaybook> items, OperationsPlaybook updated)
    {
        var index = items.FindIndex(item => item.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(updated); else items[index] = updated;
    }

    private static string Limit(string value) => value.Length <= 1000 ? value : value[..1000];
}
