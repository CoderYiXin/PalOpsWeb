using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Notifications;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.ServerRuntime;

public sealed class PalServerRuntimeMonitorService(
    IPalServerRuntimeCoordinator coordinator,
    IOptions<AppRuntimeOptions> options,
    INotificationAlertPolicyService alerts,
    ILogger<PalServerRuntimeMonitorService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("server-runtime-monitor", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.RuntimeMonitorIntervalSeconds, 5, 60));
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "server-runtime-monitor",
                anyOf: OperationalCapability.Core,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("server-runtime-monitor");
            try
            {
                var previous = coordinator.Current;
                var current = await coordinator.RefreshAsync(false, stoppingToken);
                await coordinator.HandleMonitorTransitionAsync(previous, current, stoppingToken);
                await alerts.ObserveRuntimeAsync(current, stoppingToken);
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                coordinator.ReportMonitorFailure(ex);
                logger.LogDebug("PalServer runtime monitor will retry after {Interval}.", interval);
                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
