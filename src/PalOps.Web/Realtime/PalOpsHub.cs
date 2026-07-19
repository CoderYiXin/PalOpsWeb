using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Realtime;

[Authorize]
public sealed class PalOpsHub(
    IRealtimeConnectionRegistry connections,
    IPalServerRuntimeCoordinator runtime) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name ?? "unknown";
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value ?? "Viewer";
        connections.Add(Context.ConnectionId, userName, role);
        await Clients.Caller.SendAsync("RuntimeSnapshotUpdated", runtime.Current, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SetMetricRefreshMode(string mode)
    {
        var preference = connections.SetMode(Context.ConnectionId, mode);
        await Clients.Caller.SendAsync("MetricRefreshPreferenceChanged", preference, Context.ConnectionAborted);
    }

    public async Task SetPageVisibility(bool visible)
    {
        var preference = connections.SetVisibility(Context.ConnectionId, visible);
        await Clients.Caller.SendAsync("MetricRefreshPreferenceChanged", preference, Context.ConnectionAborted);
    }

    public async Task RequestRuntimeSnapshot()
    {
        var snapshot = await runtime.RefreshAsync(true, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("RuntimeSnapshotUpdated", snapshot, Context.ConnectionAborted);
    }
}
