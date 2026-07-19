using Microsoft.AspNetCore.SignalR;
using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Realtime;

public sealed class RealtimeSnapshotDispatcherService(
    IHubContext<PalOpsHub> hub,
    IRealtimeConnectionRegistry connections,
    IPalServerRuntimeCoordinator runtime,
    IPalOpsEventBus eventBus,
    ILogger<RealtimeSnapshotDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = eventBus.Subscribe("signalr", 2000);
        var snapshots = DispatchSnapshotsAsync(stoppingToken);
        var events = DispatchEventsAsync(subscription, stoppingToken);
        await Task.WhenAll(snapshots, events);
    }

    private async Task DispatchSnapshotsAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTimeOffset.UtcNow;
            var due = connections.GetDue(now);
            if (due.Count == 0) continue;

            PalServerRuntimeSnapshot snapshot;
            try
            {
                snapshot = await runtime.RefreshAsync(false, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Unable to refresh runtime snapshot for realtime clients.");
                snapshot = runtime.Current;
            }

            foreach (var preference in due)
            {
                try
                {
                    await hub.Clients.Client(preference.ConnectionId)
                        .SendAsync("RuntimeSnapshotUpdated", snapshot, stoppingToken);
                    connections.MarkSent(preference.ConnectionId, now);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Unable to dispatch runtime snapshot to connection {ConnectionId}.", preference.ConnectionId);
                }
            }
        }
    }

    private async Task DispatchEventsAsync(IPalOpsEventSubscription subscription, CancellationToken stoppingToken)
    {
        await foreach (var palOpsEvent in subscription.ReadAllAsync(stoppingToken))
        {
            await hub.Clients.All.SendAsync("PalOpsEventReceived", palOpsEvent, stoppingToken);
            if (palOpsEvent.EventType.StartsWith("server.", StringComparison.Ordinal))
            {
                await hub.Clients.All.SendAsync("ServerOperationChanged", new
                {
                    state = runtime.Current.State,
                    operation = runtime.Current.ActiveOperation
                }, stoppingToken);
            }
        }
    }
}
