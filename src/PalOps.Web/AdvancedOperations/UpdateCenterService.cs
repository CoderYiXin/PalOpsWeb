using PalOps.Web.Backups;
using PalOps.Web.Health;
using PalOps.Web.Maintenance;
using PalOps.Web.PluginManagement;
using PalOps.Web.Versioning;

namespace PalOps.Web.AdvancedOperations;

public interface IUpdateCenterService
{
    Task<UpdateCenterDashboard> GetDashboardAsync(bool forceRefresh, CancellationToken cancellationToken = default);
    Task<UpdatePreflightResult> RunPreflightAsync(CancellationToken cancellationToken = default);
    Task<UpdatePlanRecord> UpsertPlanAsync(string? id, UpdatePlanWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task<UpdatePlanRecord> ApprovePlanAsync(string id, UpdatePlanExecuteRequest request, string actor, CancellationToken cancellationToken = default);
    Task DeletePlanAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class UpdateCenterService(
    IAdvancedOperationsRepository repository,
    IPlatformVersionService platformVersion,
    IPalDefenderVersionService palDefenderVersion,
    IPluginInventoryScanner pluginScanner,
    IBackupService backupService,
    ISystemHealthService health,
    IMaintenanceExecutionService maintenance,
    AdvancedOperationsValidator validator,
    ILogger<UpdateCenterService> logger) : IUpdateCenterService
{
    public async Task<UpdateCenterDashboard> GetDashboardAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        var components = new List<UpdateComponentStatus>();
        var now = DateTimeOffset.UtcNow;
        try
        {
            var platform = await platformVersion.CheckAsync(forceRefresh, cancellationToken);
            components.Add(new("PalOps Web", platform.CurrentVersion, platform.LatestVersion, platform.ComparisonStatus, platform.UpdateAvailable, platform.Message ?? string.Empty, platform.CheckedAt));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            components.Add(new("PalOps Web", string.Empty, string.Empty, AdvancedOperationStatus.Unknown, false, Limit(ex.Message), now));
        }
        try
        {
            var defender = await palDefenderVersion.CheckAsync(forceRefresh, cancellationToken);
            components.Add(new("PalDefender", defender.CurrentVersion, defender.LatestVersion,
                defender.UpdateAvailable ? "update-available" : defender.ComparisonAvailable ? "up-to-date" : AdvancedOperationStatus.Unknown,
                defender.UpdateAvailable, defender.Message ?? string.Empty, defender.CheckedAt));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            components.Add(new("PalDefender", string.Empty, string.Empty, AdvancedOperationStatus.Unknown, false, Limit(ex.Message), now));
        }
        try
        {
            var plugins = await pluginScanner.ScanAsync(cancellationToken);
            components.Add(new("PalServer", plugins.GameVersion, string.Empty, string.IsNullOrWhiteSpace(plugins.GameVersion) ? AdvancedOperationStatus.Unknown : "detected", false,
                plugins.ServerRunning ? "服务器正在运行。" : "服务器当前未运行。", plugins.ScannedAt));
            components.AddRange(plugins.Packages.Select(item => new UpdateComponentStatus(
                $"Plugin: {item.Name}", item.Version, item.LatestVersion,
                item.UpdateAvailable ? "update-available" : item.Compatibility,
                item.UpdateAvailable, item.ReleaseMessage ?? item.CompatibilityMessage, plugins.ScannedAt)));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Unable to scan plugins for update center.");
            components.Add(new("Plugins", string.Empty, string.Empty, AdvancedOperationStatus.Unknown, false, Limit(ex.Message), now));
        }

        var preflight = await RunPreflightAsync(cancellationToken);
        var state = await repository.ReadAsync(cancellationToken);
        return new(components, preflight, state.UpdatePlans.OrderByDescending(static item => item.UpdatedAt).ToArray(), DateTimeOffset.UtcNow);
    }

    public async Task<UpdatePreflightResult> RunPreflightAsync(CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        await health.RefreshAsync(cancellationToken);
        foreach (var component in health.Components)
        {
            if (component.Status == "unavailable") blockers.Add($"组件不可用：{component.Name}。{component.Message}");
            else if (component.Status != "healthy") warnings.Add($"组件状态异常：{component.Name} = {component.Status}。{component.Message}");
        }
        if (maintenance.IsBusy) blockers.Add("维护中心当前正在执行任务。 ");
        try
        {
            var summary = await backupService.GetSummaryAsync(cancellationToken);
            if (!summary.LatestCreatedAt.HasValue) blockers.Add("尚无可用备份。 ");
            else if (DateTimeOffset.UtcNow - summary.LatestCreatedAt.Value > TimeSpan.FromDays(2)) warnings.Add("最近备份已超过 48 小时。 ");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            blockers.Add("无法读取备份状态：" + Limit(ex.Message));
        }
        var allowed = blockers.Count == 0;
        return new(allowed, allowed ? (warnings.Count == 0 ? AdvancedOperationStatus.Healthy : AdvancedOperationStatus.Warning) : AdvancedOperationStatus.Critical,
            blockers, warnings, DateTimeOffset.UtcNow);
    }

    public Task<UpdatePlanRecord> UpsertPlanAsync(string? id, UpdatePlanWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var component = validator.ValidateName(request.TargetComponent, nameof(request.TargetComponent), 160);
        var targetVersion = validator.ValidateName(request.TargetVersion, nameof(request.TargetVersion), 80);
        if (!request.CompatibilityAcknowledged) throw new ArgumentException("Compatibility risk acknowledgement is required.", nameof(request.CompatibilityAcknowledged));
        var now = DateTimeOffset.UtcNow;
        return repository.MutateAsync(state =>
        {
            var existing = string.IsNullOrWhiteSpace(id) ? null : state.UpdatePlans.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var plan = new UpdatePlanRecord(
                existing?.Id ?? Guid.NewGuid().ToString("N"), name, component, targetVersion,
                existing?.Status ?? AdvancedOperationStatus.Pending, true, validator.LimitText(request.Note, 1000),
                existing?.CreatedAt ?? now, now, existing?.CreatedBy ?? actor, existing?.LastMessage ?? "等待预检和人工审批。 ");
            Replace(state.UpdatePlans, plan);
            return plan;
        }, cancellationToken);
    }

    public async Task<UpdatePlanRecord> ApprovePlanAsync(string id, UpdatePlanExecuteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        AdvancedOperationsValidator.RequireConfirmation(request.Confirmation, "RUN UPDATE PLAN");
        var preflight = await RunPreflightAsync(cancellationToken);
        return await repository.MutateAsync(state =>
        {
            var existing = state.UpdatePlans.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                           ?? throw new KeyNotFoundException("Update plan not found.");
            var updated = existing with
            {
                Status = preflight.Allowed ? "approved" : "blocked",
                UpdatedAt = DateTimeOffset.UtcNow,
                LastMessage = preflight.Allowed
                    ? $"由 {actor} 批准。PalOps 已完成备份与健康预检；实际组件替换仍需在维护窗口执行。"
                    : string.Join(" ", preflight.BlockingReasons)
            };
            Replace(state.UpdatePlans, updated);
            return updated;
        }, cancellationToken);
    }

    public async Task DeletePlanAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await repository.MutateAsync(state =>
        {
            var removed = state.UpdatePlans.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Update plan not found.");
            return true;
        }, cancellationToken);
    }

    private static void Replace(List<UpdatePlanRecord> items, UpdatePlanRecord updated)
    {
        var index = items.FindIndex(item => item.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(updated); else items[index] = updated;
    }

    private static string Limit(string value) => value.Length <= 1000 ? value : value[..1000];
}
