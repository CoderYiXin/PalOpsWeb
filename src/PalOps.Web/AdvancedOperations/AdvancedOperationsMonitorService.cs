using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;
namespace PalOps.Web.AdvancedOperations;

public sealed class AdvancedOperationsMonitorService(
    IIncidentCenterService incidents,
    ILogger<AdvancedOperationsMonitorService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(2);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("advanced-operations-monitor", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await readinessGate.WaitUntilReadyAsync(
                "advanced-operations-monitor",
                anyOf: OperationalCapability.Core,
                cancellationToken: stoppingToken);
            await Task.Delay(InitialDelay, stoppingToken);
            await EvaluateAsync(stoppingToken);
            using var timer = new PeriodicTimer(EvaluationInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await readinessGate.WaitUntilReadyAsync(
                    "advanced-operations-monitor",
                    anyOf: OperationalCapability.Core,
                    cancellationToken: stoppingToken);
                workerSupervisor.Heartbeat("advanced-operations-monitor");
                await EvaluateAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var changed = await incidents.EvaluateHealthAsync(cancellationToken);
            if (changed > 0)
                logger.LogInformation("Advanced operations health evaluation changed {ChangedCount} incident records.", changed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Advanced operations health evaluation failed.");
        }
    }
}
