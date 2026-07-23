namespace PalOps.Web.Platform.Tasks;

public static class PlatformTaskStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timedOut";
    public const string Interrupted = "interrupted";

    public static bool IsTerminal(string status) => status is Completed or Failed or Cancelled or TimedOut or Interrupted;
}

public sealed record PlatformTaskRecord(
    string Id,
    string Type,
    string Name,
    string? ResourceKey,
    int Priority,
    string Status,
    int Progress,
    string Stage,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    int Attempt,
    int MaximumAttempts,
    int TimeoutSeconds,
    string RequestedBy,
    string RemoteIp,
    string? CorrelationId,
    string? ParentTaskId,
    bool CanCancel,
    bool CanRetry,
    string? ErrorCode,
    string? ErrorDetail,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PlatformTaskDashboard(
    IReadOnlyList<PlatformTaskRecord> Items,
    int Queued,
    int Running,
    int FailedLast24Hours,
    int CompletedLast24Hours,
    DateTimeOffset GeneratedAt);

public sealed record PlatformTaskSubmission(
    string Type,
    string Name,
    string RequestedBy,
    string RemoteIp,
    string? ResourceKey = null,
    int Priority = 0,
    int? TimeoutSeconds = null,
    int MaximumAttempts = 1,
    string? CorrelationId = null,
    string? ParentTaskId = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? TaskId = null);

public delegate Task PlatformTaskHandler(PlatformTaskExecutionContext context, CancellationToken cancellationToken);

public sealed class PlatformTaskExecutionContext(
    string taskId,
    int attempt,
    Func<int, string?, string?, CancellationToken, ValueTask> reportProgress)
{
    public string TaskId { get; } = taskId;
    public int Attempt { get; } = attempt;

    public ValueTask ReportProgressAsync(
        int progress,
        string? stage = null,
        string? message = null,
        CancellationToken cancellationToken = default) =>
        reportProgress(Math.Clamp(progress, 0, 100), stage, message, cancellationToken);
}

public interface IPlatformTaskRepository
{
    Task RecoverInterruptedAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(PlatformTaskRecord record, CancellationToken cancellationToken = default);
    Task<PlatformTaskRecord?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformTaskRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default);
}

public interface IPlatformTaskCoordinator
{
    Task<PlatformTaskRecord> EnqueueAsync(
        PlatformTaskSubmission submission,
        PlatformTaskHandler handler,
        CancellationToken cancellationToken = default);
    Task<PlatformTaskDashboard> GetDashboardAsync(int limit = 200, CancellationToken cancellationToken = default);
    Task<PlatformTaskRecord?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default);
    Task<PlatformTaskRecord?> RetryAsync(string id, CancellationToken cancellationToken = default);
}
