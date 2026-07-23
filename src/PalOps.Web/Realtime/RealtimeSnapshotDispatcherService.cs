using Microsoft.AspNetCore.SignalR;
using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Realtime;

public sealed class RealtimeSnapshotDispatcherService(
    IHubContext<PalOpsHub> hub,
    IRealtimeConnectionRegistry connections,
    IPalServerRuntimeCoordinator runtime,
    IPalOpsEventBus eventBus,
    ILogger<RealtimeSnapshotDispatcherService> logger,
    IBackgroundWorkerSupervisor workerSupervisor,
    IOperationalReadinessGate readinessGate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workerSupervisor.RunAsync("realtime-dispatcher", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
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
            workerSupervisor.Heartbeat("realtime-dispatcher");
            var now = DateTimeOffset.UtcNow;
            var due = connections.GetDue(now);
            if (due.Count == 0) continue;

            PalServerRuntimeSnapshot snapshot;
            var readiness = await readinessGate.GetSnapshotAsync(stoppingToken).ConfigureAwait(false);
            if (!readiness.HasAnyOperationalConfiguration)
            {
                snapshot = runtime.Current;
            }
            else
            {
                try
                {
                    snapshot = await runtime.RefreshAsync(false, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Unable to refresh runtime snapshot for realtime clients.");
                    snapshot = runtime.Current;
                }
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
