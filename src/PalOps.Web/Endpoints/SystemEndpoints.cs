using PalOps.Web.Automation;
using PalOps.Web.Backups;
using PalOps.Web.Contracts;
using PalOps.Web.Health;
using PalOps.Web.Players;
using PalOps.Web.SaveGames;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system").RequireAuthorization().WithTags("System");

        group.MapGet("/overview", async (
            IServerSettingsStore settingsStore,
            ISaveIndexRepository repository,
            ISaveIndexingService indexingService,
            IPlayerAggregationService players,
            ISystemHealthService health,
            IBackupService backups,
            IAutomationRepository automationRepository,
            IAutomationExecutionService automationExecution,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var settings = await settingsStore.GetAsync(cancellationToken);
            var snapshot = await repository.GetCurrentAsync(cancellationToken);
            var onlinePlayers = 0;
            try { onlinePlayers = (await players.GetOnlinePlayersAsync(cancellationToken)).Count; } catch { }
            var backupSummary = await backups.GetSummaryAsync(cancellationToken);
            var jobs = await automationRepository.ListJobsAsync(cancellationToken);
            var running = automationExecution.RunningJobIds;
            var nextRun = jobs.Where(x => x.Enabled && x.NextRunAt.HasValue)
                .OrderBy(x => x.NextRunAt)
                .Select(x => x.NextRunAt)
                .FirstOrDefault();
            var automationSummary = new AutomationSummaryV1(
                jobs.Count,
                jobs.Count(x => x.Enabled),
                jobs.Count(x => running.Contains(x.Id)),
                jobs.Count(x => x.LastStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)),
                nextRun);
            var components = health.Components;
            var applicationStatus = components.Any(x => x.Status == "unavailable") ? "degraded" : "healthy";
            var data = new SystemOverviewV1(
                applicationStatus,
                !string.IsNullOrWhiteSpace(settings.SaveGame.WorldDirectory),
                indexingService.Status,
                snapshot?.SnapshotId,
                snapshot?.ParsedAt,
                snapshot?.Players.Count ?? 0,
                snapshot?.Items.Count ?? 0,
                snapshot?.Pals.Count ?? 0,
                snapshot?.Guilds.Count ?? 0,
                snapshot?.Bases.Count ?? 0,
                onlinePlayers,
                components,
                backupSummary,
                automationSummary);
            return Results.Ok(new ApiResponse<SystemOverviewV1>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/health/components", async (
            bool? refresh,
            ISystemHealthService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (refresh == true) await service.RefreshAsync(cancellationToken);
            return Results.Ok(new ApiResponse<IReadOnlyList<HealthComponentV1>>(service.Components, context.TraceIdentifier, []));
        });

        group.MapGet("/version", (HttpContext context) =>
        {
            var assembly = typeof(Program).Assembly.GetName();
            var data = new
            {
                application = assembly.Name,
                version = assembly.Version?.ToString() ?? "0.0.0",
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                buildTime = File.GetLastWriteTimeUtc(typeof(Program).Assembly.Location)
            };
            return Results.Ok(new ApiResponse<object>(data, context.TraceIdentifier, []));
        });

        return endpoints;
    }
}
