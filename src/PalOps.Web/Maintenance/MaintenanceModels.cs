using System.Text.Json.Serialization;

namespace PalOps.Web.Maintenance;

public static class MaintenanceStatuses
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
}

public static class MaintenanceStepKeys
{
    public const string Announcement = "announcement";
    public const string SaveWorld = "save-world";
    public const string Backup = "backup";
    public const string StopServer = "stop-server";
    public const string Script = "maintenance-script";
    public const string StartServer = "start-server";
    public const string HealthVerification = "health-verification";
}

public sealed record CrashGuardConfiguration(
    bool Enabled,
    int MaximumCrashes,
    int WindowMinutes,
    int RestartDelaySeconds,
    int OperationTimeoutSeconds,
    bool NotifyOnRestart,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public static CrashGuardConfiguration Default() => new(
        false, 3, 10, 15, 180, true, DateTimeOffset.MinValue, string.Empty);
}

public sealed record CrashGuardState(
    bool Suspended,
    bool CircuitOpen,
    DateTimeOffset? CircuitOpenedAt,
    DateTimeOffset? LastResetAt,
    DateTimeOffset? LastCrashAt,
    DateTimeOffset? LastRestartAt,
    string LastRestartOutcome,
    string LastMessage)
{
    public static CrashGuardState Default() => new(
        false, false, null, null, null, null, "never", "尚未处理崩溃事件。");
}

public sealed record CrashGuardStatus(
    string Status,
    CrashGuardConfiguration Configuration,
    CrashGuardState State,
    int CrashesInWindow,
    DateTimeOffset WindowStartedAt,
    DateTimeOffset? NextEligibleRestartAt,
    IReadOnlyList<MaintenanceCrashEvent> RecentEvents);

public sealed record MaintenanceCrashEvent(
    string Id,
    string EventType,
    DateTimeOffset OccurredAt,
    int? ProcessId,
    string Outcome,
    string Message,
    string? OperationId,
    string? ErrorCode);

public sealed record MaintenancePlan(
    string Id,
    string Name,
    bool Enabled,
    string ScheduleType,
    string ScheduleExpression,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string LastStatus,
    string LastMessage,
    bool AnnouncementEnabled,
    int AnnouncementCountdownSeconds,
    string AnnouncementMessage,
    bool SaveWorld,
    bool CreateBackup,
    string BackupNote,
    bool StopServer,
    bool ScriptEnabled,
    string ScriptPath,
    string ScriptArguments,
    int ScriptTimeoutSeconds,
    bool StartServer,
    bool VerifyProcess,
    bool VerifyRest,
    bool VerifyRcon,
    int HealthTimeoutSeconds,
    int HealthRetrySeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public static MaintenancePlan Default(string id, string userName, DateTimeOffset now) => new(
        id,
        "例行维护",
        false,
        "manual",
        string.Empty,
        null,
        null,
        "never",
        "尚未执行。",
        true,
        60,
        "服务器将在 {seconds} 秒后进入维护，请及时返回安全区域。",
        true,
        true,
        "维护前自动备份",
        true,
        false,
        string.Empty,
        string.Empty,
        1800,
        true,
        true,
        true,
        true,
        180,
        5,
        now,
        now,
        userName);
}

public sealed record MaintenanceRunStep(
    string Key,
    string Name,
    string Status,
    int ProgressStart,
    int ProgressEnd,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string Message,
    string Output);

public sealed record MaintenanceRun(
    string Id,
    string PlanId,
    string PlanName,
    string Trigger,
    string Status,
    string CurrentStage,
    int Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string StartedBy,
    string RemoteIp,
    string Message,
    string? ErrorCode,
    IReadOnlyList<MaintenanceRunStep> Steps)
{
    [JsonIgnore]
    public bool IsTerminal => Status is MaintenanceStatuses.Succeeded or MaintenanceStatuses.Failed or MaintenanceStatuses.Cancelled;
}

public sealed record MaintenanceDashboard(
    CrashGuardStatus CrashGuard,
    MaintenanceRun? ActiveRun,
    IReadOnlyList<MaintenancePlan> Plans,
    IReadOnlyList<MaintenanceRun> RecentRuns,
    bool ExecutionBusy);

public sealed record CrashGuardConfigurationWriteRequest(
    bool Enabled,
    int MaximumCrashes,
    int WindowMinutes,
    int RestartDelaySeconds,
    int OperationTimeoutSeconds,
    bool NotifyOnRestart);

public sealed record MaintenancePlanWriteRequest(
    string Name,
    bool Enabled,
    string ScheduleType,
    string ScheduleExpression,
    bool AnnouncementEnabled,
    int AnnouncementCountdownSeconds,
    string AnnouncementMessage,
    bool SaveWorld,
    bool CreateBackup,
    string BackupNote,
    bool StopServer,
    bool ScriptEnabled,
    string ScriptPath,
    string ScriptArguments,
    int ScriptTimeoutSeconds,
    bool StartServer,
    bool VerifyProcess,
    bool VerifyRest,
    bool VerifyRcon,
    int HealthTimeoutSeconds,
    int HealthRetrySeconds);

public sealed record MaintenanceRunRequest(string Confirmation);
public sealed record MaintenanceReasonRequest(string Reason);
public sealed record CrashGuardResetRequest(string Confirmation, string Reason);

internal sealed class MaintenanceStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public CrashGuardConfiguration CrashGuardConfiguration { get; set; } = CrashGuardConfiguration.Default();
    public CrashGuardState CrashGuardState { get; set; } = CrashGuardState.Default();
    public List<MaintenancePlan> Plans { get; set; } = [];
    public List<MaintenanceRun> Runs { get; set; } = [];
    public List<MaintenanceCrashEvent> CrashEvents { get; set; } = [];
}
