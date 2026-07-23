using PalOps.Web.Settings;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Automation;

public sealed class AutomationSchedulerService(
    IAutomationRepository repository,
    IAutomationExecutionService executionService,
    IServerSettingsStore settingsStore,
    ILogger<AutomationSchedulerService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("automation-scheduler", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "automation-scheduler",
                allOf: OperationalCapability.Automation,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("automation-scheduler");
            var delaySeconds = 15;
            try
            {
                var settings = await settingsStore.GetAsync(stoppingToken);
                delaySeconds = settings.Automation.PollIntervalSeconds;
                if (settings.Automation.Enabled)
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var job in await repository.ListJobsAsync(stoppingToken))
                    {
                        if (!job.Enabled || !job.NextRunAt.HasValue || job.NextRunAt.Value > now) continue;
                        _ = RunDetachedAsync(job, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Automation scheduler iteration failed."); }

            try { await workerSupervisor.DelayWithHeartbeatAsync("automation-scheduler", TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 5, 300)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunDetachedAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        try { await executionService.ExecuteAsync(job, "scheduler", cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Detached automation execution failed. JobId={JobId}", job.Id); }
    }
}
