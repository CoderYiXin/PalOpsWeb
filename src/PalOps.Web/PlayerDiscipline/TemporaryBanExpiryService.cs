using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.PlayerDiscipline;

public sealed class TemporaryBanExpiryService(
    IPlayerDisciplineService service,
    IBackgroundWorkerSupervisor workerSupervisor,
    ILogger<TemporaryBanExpiryService> logger,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("temporary-ban-expiry", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await readinessGate.WaitUntilReadyAsync(
            "temporary-ban-expiry",
            allOf: OperationalCapability.PalDefender,
            cancellationToken: stoppingToken);
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (true)
        {
            workerSupervisor.Heartbeat("temporary-ban-expiry");
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            await readinessGate.WaitUntilReadyAsync(
                "temporary-ban-expiry",
                allOf: OperationalCapability.PalDefender,
                cancellationToken: stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try { await service.UnbanExpiredAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Temporary ban expiry scan failed."); }
    }
}

public sealed class PlayerIdentitySyncService(
    IPlayerDisciplineService service,
    IBackgroundWorkerSupervisor workerSupervisor,
    ILogger<PlayerIdentitySyncService> logger,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("player-identity-sync", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "player-identity-sync",
                allOf: OperationalCapability.PalDefender,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("player-identity-sync");
            try { await service.SyncIdentitiesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogDebug(ex, "Player identity synchronization failed."); }
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
}
