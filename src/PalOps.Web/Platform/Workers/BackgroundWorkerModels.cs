namespace PalOps.Web.Platform.Workers;

public sealed record BackgroundWorkerSnapshot(
    string Name,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeatAt,
    DateTimeOffset? LastFailureAt,
    int RestartCount,
    string? LastError,
    bool IsStale);

public interface IBackgroundWorkerSupervisor
{
    Task RunAsync(string name, Func<CancellationToken, Task> workerLoop, CancellationToken stoppingToken);
    Task DelayWithHeartbeatAsync(string name, TimeSpan delay, CancellationToken cancellationToken);
    void Heartbeat(string name);
    IReadOnlyList<BackgroundWorkerSnapshot> GetSnapshots();
}
