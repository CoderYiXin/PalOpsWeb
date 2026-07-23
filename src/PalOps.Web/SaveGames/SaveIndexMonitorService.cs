using PalOps.Web.SaveGames.Index;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Settings;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.SaveGames;

public sealed class SaveIndexMonitorService(
    IServerSettingsStore settingsStore,
    ISaveSourceResolver sourceResolver,
    ISaveIndexRepository repository,
    ISaveIndexingService indexingService,
    IPalServerRuntimeCoordinator runtimeCoordinator,
    ILogger<SaveIndexMonitorService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    private static readonly TimeSpan MinimumAutomaticInterval = TimeSpan.FromMinutes(10);
    private SaveFileFingerprint? _lastObserved;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("save-index-monitor", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "save-index-monitor",
                allOf: OperationalCapability.SaveAutoIndex,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("save-index-monitor");
            var delay = MinimumAutomaticInterval;
            try
            {
                var settings = await settingsStore.GetAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Clamp(
                    settings.SaveGame.PollIntervalSeconds,
                    (int)MinimumAutomaticInterval.TotalSeconds,
                    3600));

                if (settings.SaveGame.AutoIndex && !string.IsNullOrWhiteSpace(settings.SaveGame.WorldDirectory))
                {
                    var current = await repository.GetCurrentAsync(stoppingToken);
                    var onlinePlayers = runtimeCoordinator.Current.LiveStatus?.OnlinePlayers;

                    // Preserve one initial automatic parse so a fresh installation has a usable
                    // snapshot. Once a snapshot exists, an explicitly known zero-player state
                    // suppresses repeated parsing until somebody joins again. A null count means
                    // the live API is unavailable, so file-change detection remains active.
                    if (current is not null && onlinePlayers == 0)
                    {
                        logger.LogDebug(
                            "Automatic save indexing skipped because no players are online. Next check in {Delay}.",
                            delay);
                    }
                    else
                    {
                        var source = sourceResolver.ResolveConfigured(settings.SaveGame);
                        var fingerprint = SaveFileFingerprint.Read(source.LevelPath);

                        // Avoid a redundant parse immediately after PalOps restarts when the current
                        // index was produced after the last Level.sav write.
                        if (_lastObserved is null
                            && current is not null
                            && fingerprint.LastWriteTimeUtc <= current.CreatedAt.UtcDateTime)
                        {
                            _lastObserved = fingerprint;
                        }
                        else if (_lastObserved != fingerprint || current is null)
                        {
                            var result = await indexingService.TriggerAsync("file-monitor", stoppingToken);
                            if (result.Started) _lastObserved = fingerprint;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Save monitor could not inspect the configured world directory.");
            }

            try
            {
                await workerSupervisor.DelayWithHeartbeatAsync("save-index-monitor", delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
