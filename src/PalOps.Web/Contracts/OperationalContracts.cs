namespace PalOps.Web.Contracts;

public sealed record HealthComponentV1(
    string Name,
    string Status,
    long? LatencyMs,
    DateTimeOffset CheckedAt,
    string? Message);

public sealed record HealthCategorySummaryV1(
    string Name,
    string Status,
    int Healthy,
    int Total);

public sealed record HostRuntimeHealthV1(
    long WorkingSetBytes,
    long ManagedHeapBytes,
    int ThreadCount,
    long DiskFreeBytes,
    long DiskTotalBytes,
    double DiskFreePercent,
    long UptimeSeconds);

public sealed record TaskCenterHealthV1(int Queued, int Running, int FailedLast24Hours, int CompletedLast24Hours);
public sealed record CacheHealthV1(long Hits, long Misses, double HitRate, int Entries, long Evictions);
public sealed record WorkerHealthV1(int Total, int Running, int Failed, int Stale, int Restarts);
public sealed record LoggingHealthV1(long WrittenEntries, long DroppedEntries, int FileCount, long TotalSizeBytes, DateTimeOffset? LastWriteAt);

public sealed record SystemHealthDashboardV1(
    string OverallStatus,
    int Score,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HealthComponentV1> Components,
    IReadOnlyList<HealthCategorySummaryV1> Categories,
    HostRuntimeHealthV1 Host,
    TaskCenterHealthV1 Tasks,
    CacheHealthV1 Cache,
    WorkerHealthV1 Workers,
    LoggingHealthV1 Logging,
    IReadOnlyList<string> Remediations);

public sealed record BackupSummaryV1(
    string Directory,
    int Count,
    long TotalSizeBytes,
    DateTimeOffset? LatestCreatedAt,
    bool RestoreEnabled,
    bool RestoreMarkerPresent);

public sealed record BackupRecordV1(
    string Id,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string Sha256,
    string Status,
    string Note,
    string WorldId,
    int FileCount,
    bool ExecuteSaveFirst,
    DateTimeOffset? VerifiedAt,
    string? Error);

public sealed record CreateBackupRequest(string? Note, bool? ExecuteSaveFirst);
public sealed record RestoreBackupRequest(string Confirmation, string BackupName);
public sealed record BackupVerificationV1(bool Valid, string Status, string Sha256, long SizeBytes, DateTimeOffset VerifiedAt, string? Message);
public sealed record BackupRestorePreflightV1(bool Allowed, IReadOnlyList<string> BlockingReasons, string MarkerPath, string WorldDirectory, string BackupFile);

public sealed record AutomationJobV1(
    string Id,
    string Name,
    string JobType,
    bool Enabled,
    string ScheduleType,
    string ScheduleExpression,
    string PayloadJson,
    string RiskLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    string LastStatus,
    string LastMessage,
    int ConsecutiveFailures,
    bool Running);

public sealed record AutomationJobWriteRequest(
    string Name,
    string JobType,
    bool Enabled,
    string ScheduleType,
    string ScheduleExpression,
    string? PayloadJson,
    string? Confirmation);

public sealed record AutomationRunRecordV1(
    string Id,
    string JobId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string Message,
    long DurationMs);

public sealed record AutomationSummaryV1(
    int TotalJobs,
    int EnabledJobs,
    int RunningJobs,
    int FailedJobs,
    DateTimeOffset? NextRunAt);

public sealed record GuildMemberV1(
    string PlayerUid,
    string UserId,
    string Name,
    int Level,
    bool Online,
    bool Leader,
    DateTimeOffset? LastSeenAt);

public sealed record BaseCampV1(
    string BaseId,
    string GuildId,
    string GuildName,
    double? X,
    double? Y,
    double? Z,
    int WorkerCount,
    int MapObjectCount,
    DateTimeOffset SnapshotAt,
    string AssociationType,
    string PositionSource,
    IReadOnlyList<string> RelatedPlayerUids,
    string? AssociationReason,
    bool PositionResolved,
    string MapEntityId);

public sealed record GuildDetailV1(
    string GuildId,
    string Name,
    string? LeaderPlayerUid,
    string LeaderName,
    int Level,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset SnapshotAt,
    IReadOnlyList<GuildMemberV1> Members,
    IReadOnlyList<BaseCampV1> Bases);

public sealed record CustomMapMarkerV1(
    string Id,
    string Label,
    string Description,
    double X,
    double Y,
    double Z,
    string Category,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string MapLayer,
    string CoordinateSpace,
    string? CoordinateWarning);

public sealed record CustomMapMarkerWriteRequest(
    string Label,
    string? Description,
    double X,
    double Y,
    double Z,
    string? Category,
    string? MapLayer,
    string? CoordinateSpace);

public sealed record PalServerRuntimeConfigurationWriteRequest(
    string LaunchMode,
    string ExecutablePath,
    string ScriptPath,
    string WorkingDirectory,
    string Arguments,
    int StartupTimeoutSeconds,
    int ShutdownTimeoutSeconds,
    int SaveWaitSeconds,
    int RestartCooldownSeconds);

public sealed record ForceStopRequest(string Confirmation, string Reason);
