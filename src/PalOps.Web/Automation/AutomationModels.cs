namespace PalOps.Web.Automation;

public static class AutomationJobTypes
{
    public const string SaveWorld = "saveWorld";
    public const string Broadcast = "broadcast";
    public const string CreateBackup = "createBackup";
    public const string ReloadPalDefender = "reloadPalDefender";
    public const string ScheduledShutdown = "scheduledShutdown";
    public const string HealthCheck = "healthCheck";
    public const string ParseSave = "parseSave";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SaveWorld, Broadcast, CreateBackup, ReloadPalDefender, ScheduledShutdown, HealthCheck, ParseSave
    };
}

public sealed record AutomationJob(
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
    int ConsecutiveFailures);

public sealed record AutomationRunRecord(
    string Id,
    string JobId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string Message,
    long DurationMs);
