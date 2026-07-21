using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Maintenance;

public interface IServerOperationWaiter
{
    Task<ServerOperationSnapshot> WaitAsync(
        string operationId,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}

public sealed class ServerOperationWaiter(IPalServerRuntimeCoordinator coordinator) : IServerOperationWaiter
{
    public async Task<ServerOperationSnapshot> WaitAsync(
        string operationId,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 15, 1800));
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = coordinator.FindOperation(operationId);
            if (operation is not null && operation.State is "completed" or "failed") return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"服务器操作 {operationId} 未在 {timeoutSeconds} 秒内完成。");
    }
}
