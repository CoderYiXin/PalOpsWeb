using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.Players;
using PalOps.Web.Statistics;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Realtime;

public sealed class PlayerPresenceMonitorService(
    IServiceScopeFactory scopeFactory,
    IPalOpsEventBus eventBus,
    IPalOpsEventPublisher publisher,
    IStatisticsRecorder statisticsRecorder,
    ILogger<PlayerPresenceMonitorService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    private IReadOnlyDictionary<string, PlayerResponse>? _baseline;
    private int _resetBaseline;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("player-presence-monitor", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await using var subscription = eventBus.Subscribe("player-presence-control", 100);
        var control = ListenForServerStartsAsync(subscription, stoppingToken);
        var polling = PollPlayersAsync(stoppingToken);
        await Task.WhenAll(control, polling);
    }

    private async Task ListenForServerStartsAsync(IPalOpsEventSubscription subscription, CancellationToken stoppingToken)
    {
        await foreach (var palOpsEvent in subscription.ReadAllAsync(stoppingToken))
        {
            if (palOpsEvent.EventType == "server.started" || palOpsEvent.EventType == "server.restarted")
                Interlocked.Exchange(ref _resetBaseline, 1);
        }
    }

    private async Task PollPlayersAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            await readinessGate.WaitUntilReadyAsync(
                "player-presence-monitor",
                anyOf: OperationalCapability.PlayerSources,
                cancellationToken: stoppingToken);
            workerSupervisor.Heartbeat("player-presence-monitor");
            try
            {
                using var scope = scopeFactory.CreateScope();
                var players = await scope.ServiceProvider.GetRequiredService<IPlayerAggregationService>()
                    .GetOnlinePlayersAsync(stoppingToken);
                var current = players
                    .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                await statisticsRecorder.RecordPlayerPresenceAsync(
                    current.Values.ToArray(),
                    DateTimeOffset.UtcNow,
                    stoppingToken);

                if (_baseline is null || Interlocked.Exchange(ref _resetBaseline, 0) == 1)
                {
                    _baseline = current;
                }
                else
                {
                    foreach (var entry in current.Where(entry => !_baseline.ContainsKey(entry.Key)))
                        await publisher.PublishAsync(PlayerEvent("player.online", entry.Value), stoppingToken);
                    foreach (var entry in _baseline.Where(entry => !current.ContainsKey(entry.Key)))
                        await publisher.PublishAsync(PlayerEvent("player.offline", entry.Value), stoppingToken);
                    _baseline = current;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Player presence query failed; retaining the previous baseline.");
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

    private static string Key(PlayerResponse player)
    {
        var value = !string.IsNullOrWhiteSpace(player.PlayerUid)
            ? player.PlayerUid
            : !string.IsNullOrWhiteSpace(player.UserId)
                ? player.UserId
                : player.Name;
        return value.Trim().ToLowerInvariant();
    }

    private static PalOpsEvent PlayerEvent(string type, PlayerResponse player) => PalOpsEvent.Create(
        type,
        "information",
        player: new Dictionary<string, object?>
        {
            ["name"] = player.Name,
            ["uid"] = player.PlayerUid,
            ["userId"] = player.UserId,
            ["guildName"] = player.GuildName
        });
}
