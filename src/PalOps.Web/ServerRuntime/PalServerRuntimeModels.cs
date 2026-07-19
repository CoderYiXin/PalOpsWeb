using System.Text.Json.Serialization;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.ServerRuntime;

public enum PalServerRuntimeState
{
    Stopped, Discovering, Starting, Running, Saving, Stopping, Restarting,
    StopTimedOut, ExitedUnexpectedly, IdentityUnknown, Faulted
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PalServerLaunchMode { Script, Executable }

public sealed record PalServerRuntimeConfiguration(
    int SchemaVersion,
    bool Confirmed,
    PalServerLaunchMode LaunchMode,
    string ExecutablePath,
    string ScriptPath,
    string WorkingDirectory,
    string Arguments,
    int StartupTimeoutSeconds,
    int ShutdownTimeoutSeconds,
    int SaveWaitSeconds,
    int RestartCooldownSeconds,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public static PalServerRuntimeConfiguration Unconfirmed(int startup = 90, int shutdown = 120, int saveWait = 5, int cooldown = 3)
        => new(1, false, PalServerLaunchMode.Executable, string.Empty, string.Empty, string.Empty, string.Empty,
            startup, shutdown, saveWait, cooldown, DateTimeOffset.MinValue, string.Empty);
}

public sealed record PalServerDiscoveryCandidate(
    string CandidateId,
    PalServerLaunchMode LaunchMode,
    string ExecutablePath,
    string ScriptPath,
    string WorkingDirectory,
    string Arguments,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record PalServerDiscoveryResult(
    string WorldDirectory,
    string? ServerRoot,
    IReadOnlyList<PalServerDiscoveryCandidate> Candidates,
    IReadOnlyList<string> Warnings);

public sealed record PalServerProcessSnapshot(
    int? ProcessId,
    string State,
    bool IdentityVerified,
    string IdentityReason,
    string? ExecutablePath,
    DateTimeOffset? StartedAt,
    TimeSpan? Uptime,
    int ThreadCount,
    int? ParentProcessId);

public sealed record PalServerVerifiedProcess(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    int? ParentProcessId,
    bool IsLauncher);

public sealed record PalServerLiveStatusSnapshot(
    string? ServerName,
    int? OnlinePlayers,
    int? MaximumPlayers,
    double? ServerFps,
    DateTimeOffset CapturedAt,
    string Source,
    string? ErrorMessage);

public sealed record HostMetricsSnapshot(
    DateTimeOffset CapturedAt,
    double SystemCpuPercent,
    long SystemMemoryUsedBytes,
    long SystemMemoryTotalBytes,
    double SystemMemoryPercent,
    double? PalServerCpuPercent,
    long? PalServerWorkingSetBytes,
    long? PalServerPrivateMemoryBytes,
    long DiskFreeBytes,
    long DiskTotalBytes,
    long? SaveDirectorySizeBytes,
    DateTimeOffset? SaveDirectorySizeMeasuredAt);

public sealed record ServerOperationSnapshot(
    string OperationId,
    string Type,
    string State,
    string Stage,
    int Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode,
    string? Message);

public sealed record ServerOperationHistoryRecord(
    string OperationId,
    string Type,
    string Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string UserName,
    string RemoteIp,
    int? ProcessId,
    string? Reason,
    string? ErrorCode,
    string? Message);

public sealed record PalServerRuntimeSnapshot(
    string State,
    PalServerProcessSnapshot Process,
    HostMetricsSnapshot? Metrics,
    PalServerLiveStatusSnapshot? LiveStatus,
    ServerOperationSnapshot? ActiveOperation,
    bool ConfigurationConfirmed,
    bool RconConfigured,
    string? LastErrorCode,
    string? LastMessage,
    DateTimeOffset CapturedAt);

public sealed class PalServerRuntimeException(
    int statusCode,
    string code,
    string message,
    string? detail = null,
    string? suggestedAction = null,
    Exception? innerException = null)
    : PalOpsApiException(statusCode, code, message, detail, suggestedAction, innerException);
