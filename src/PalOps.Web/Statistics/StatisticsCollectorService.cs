using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Statistics;

public sealed class StatisticsCollectorService(
    IPalServerRuntimeCoordinator runtime,
    IStatisticsRecorder recorder,
    IPalOpsEventBus eventBus,
    ILogger<StatisticsCollectorService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("statistics-collector", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await using var subscription = eventBus.Subscribe("statistics", 2000);
        await Task.WhenAll(
            CollectRuntimeAsync(stoppingToken),
            CollectEventsAsync(subscription, stoppingToken));
    }

    private async Task CollectRuntimeAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "statistics-collector",
                anyOf: OperationalCapability.Core,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("statistics-collector");
            try
            {
                await recorder.RecordRuntimeAsync(runtime.Current, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to record the current server statistics snapshot.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CollectEventsAsync(
        IPalOpsEventSubscription subscription,
        CancellationToken stoppingToken)
    {
        await foreach (var palOpsEvent in subscription.ReadAllAsync(stoppingToken))
        {
            await readinessGate.WaitUntilReadyAsync(
                "statistics-collector",
                anyOf: OperationalCapability.Core,
                cancellationToken: stoppingToken);
            try
            {
                await recorder.RecordEventAsync(palOpsEvent, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to record statistics event {EventType}.", palOpsEvent.EventType);
            }
        }
    }
}
