using System.Security.Cryptography;
using System.Text;

namespace PalOps.Web.AdvancedOperations;

public static class IncidentSeverity
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Information = "information";
}

public static class IncidentStatus
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
}

public static class DisasterRecoveryTargetType
{
    public const string Local = "local";
    public const string Unc = "unc";
    public const string WebDav = "webdav";
    public const string S3Compatible = "s3-compatible";
}

public static class AdvancedOperationStatus
{
    public const string Healthy = "healthy";
    public const string Warning = "warning";
    public const string Critical = "critical";
    public const string Unknown = "unknown";
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}


public sealed record ConfigurationReadinessCheck(
    string Code,
    string Label,
    string Status,
    bool Required,
    string Message,
    string ActionLabel,
    string ActionRoute);

public sealed record AdvancedModuleReadiness(
    string Module,
    string Status,
    bool Ready,
    bool Blocked,
    bool FirstRun,
    int CompletionPercent,
    string Title,
    string Description,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> MissingSettings,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<ConfigurationReadinessCheck> Checks,
    DateTimeOffset GeneratedAt);

public sealed record DiagnosticCheckResult(
    string Code,
    string Name,
    string Category,
    string Status,
    string Severity,
    long? LatencyMs,
    string Message,
    string Remediation,
    DateTimeOffset CheckedAt,
    IReadOnlyDictionary<string, string> Details);

public sealed record DiagnosticReport(
    string OverallStatus,
    int HealthyCount,
    int WarningCount,
    int CriticalCount,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DiagnosticCheckResult> Checks);

public sealed record DiagnosticSupportBundle(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record IncidentTimelineEntry(
    string Id,
    string Action,
    string Actor,
    string Message,
    DateTimeOffset OccurredAt);

public sealed record IncidentRecord(
    string Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    string Source,
    string Fingerprint,
    string Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    int OccurrenceCount,
    DateTimeOffset LastObservedAt,
    IReadOnlyList<IncidentTimelineEntry> Timeline);

public sealed record IncidentRule(
    string Id,
    string Name,
    bool Enabled,
    string SourceCode,
    IReadOnlyList<string> TriggerStatuses,
    string Severity,
    bool AutoResolve,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record IncidentCenterDashboard(
    int OpenCount,
    int AcknowledgedCount,
    int ResolvedCount,
    int CriticalCount,
    IReadOnlyList<IncidentRecord> Incidents,
    IReadOnlyList<IncidentRule> Rules,
    DateTimeOffset GeneratedAt);

public sealed record IncidentCreateRequest(
    string Title,
    string? Description,
    string Severity,
    string? Source,
    string? Fingerprint,
    string? Assignee);

public sealed record IncidentActionRequest(string Action, string? Message, string? Assignee);
public sealed record IncidentRuleWriteRequest(string Name, bool Enabled, string SourceCode, IReadOnlyList<string> TriggerStatuses, string Severity, bool AutoResolve);

public sealed record PlayerInsightRecord(
    string PlayerUid,
    string UserId,
    string Name,
    string GuildName,
    int Level,
    bool Online,
    DateTimeOffset? LastSeenAt,
    int ViolationCount,
    bool Banned,
    bool Whitelisted,
    int RiskScore,
    string RiskLevel,
    IReadOnlyList<string> Signals,
    string Notes,
    bool AdvisoryOnly);

public sealed record PlayerInsightsDashboard(
    int TotalPlayers,
    int OnlinePlayers,
    int HighRiskPlayers,
    int InactivePlayers,
    DateTimeOffset? SnapshotAt,
    IReadOnlyList<PlayerInsightRecord> Players,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt);

public sealed record PlayerInsightNote(
    string PlayerKey,
    string Notes,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record PlayerInsightNoteWriteRequest(string Notes);

public sealed record WorldGovernanceCandidate(
    string Id,
    string CandidateType,
    string Severity,
    string Title,
    string Description,
    string GuildId,
    string BaseId,
    int MemberCount,
    int WorkerCount,
    DateTimeOffset? LastActivityAt,
    string ReviewStatus,
    string ReviewNote,
    string ReviewedBy,
    DateTimeOffset? ReviewedAt,
    bool AdvisoryOnly);

public sealed record WorldGovernanceDashboard(
    int GuildCount,
    int BaseCount,
    int CandidateCount,
    int CriticalCandidateCount,
    DateTimeOffset? SnapshotAt,
    IReadOnlyList<WorldGovernanceCandidate> Candidates,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt);

public sealed record GovernanceReview(
    string CandidateId,
    string Status,
    string Note,
    string ReviewedBy,
    DateTimeOffset ReviewedAt);

public sealed record GovernanceReviewWriteRequest(string Status, string? Note);

public sealed record DisasterRecoveryTarget(
    string Id,
    string Name,
    string TargetType,
    string Endpoint,
    string CredentialReference,
    bool Enabled,
    int RetentionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string LastValidationStatus,
    string LastValidationMessage,
    DateTimeOffset? LastValidatedAt);

public sealed record DisasterRecoveryDrill(
    string Id,
    string TargetId,
    string TargetName,
    string BackupId,
    string BackupFileName,
    string Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string StartedBy);

public sealed record DisasterRecoveryDashboard(
    int EnabledTargets,
    int TotalTargets,
    int SuccessfulDrills,
    int FailedDrills,
    IReadOnlyList<DisasterRecoveryTarget> Targets,
    IReadOnlyList<DisasterRecoveryDrill> RecentDrills,
    DateTimeOffset GeneratedAt);

public sealed record DisasterRecoveryTargetWriteRequest(
    string Name,
    string TargetType,
    string Endpoint,
    string? CredentialReference,
    bool Enabled,
    int RetentionCount);

public sealed record DisasterRecoveryDrillRequest(string BackupId);

public sealed record UpdateComponentStatus(
    string Component,
    string CurrentVersion,
    string LatestVersion,
    string Status,
    bool UpdateAvailable,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record UpdatePreflightResult(
    bool Allowed,
    string Status,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CheckedAt);

public sealed record UpdatePlanRecord(
    string Id,
    string Name,
    string TargetComponent,
    string TargetVersion,
    string Status,
    bool CompatibilityAcknowledged,
    string Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedBy,
    string LastMessage);

public sealed record UpdateCenterDashboard(
    IReadOnlyList<UpdateComponentStatus> Components,
    UpdatePreflightResult Preflight,
    IReadOnlyList<UpdatePlanRecord> Plans,
    DateTimeOffset GeneratedAt);

public sealed record UpdatePlanWriteRequest(string Name, string TargetComponent, string TargetVersion, bool CompatibilityAcknowledged, string? Note);
public sealed record UpdatePlanExecuteRequest(string Confirmation);

public sealed record ConfigurationVersionSnapshot(
    string Id,
    string Name,
    string Note,
    string Sha256,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool CurrentMatch,
    string SourcePath);

public sealed record ConfigurationVersionDiff(
    string FromId,
    string ToId,
    bool Identical,
    IReadOnlyList<string> ChangedKeys,
    string LaunchArgumentsBefore,
    string LaunchArgumentsAfter);

public sealed record ConfigurationVersionDashboard(
    string CurrentSha256,
    string CurrentPath,
    IReadOnlyList<ConfigurationVersionSnapshot> Versions,
    DateTimeOffset GeneratedAt);

public sealed record ConfigurationVersionCreateRequest(string Name, string? Note);
public sealed record ConfigurationVersionRestoreRequest(string Confirmation, bool Restart);

public sealed record OperationsPlaybookStep(
    int Order,
    string Action,
    IReadOnlyDictionary<string, string> Parameters,
    bool ContinueOnError);

public sealed record OperationsPlaybook(
    string Id,
    string Name,
    string Description,
    bool Enabled,
    IReadOnlyList<OperationsPlaybookStep> Steps,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string LastStatus,
    string LastMessage,
    DateTimeOffset? LastRunAt);

public sealed record OperationsPlaybookRunStep(
    int Order,
    string Action,
    string Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record OperationsPlaybookRun(
    string Id,
    string PlaybookId,
    string PlaybookName,
    string Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string StartedBy,
    IReadOnlyList<OperationsPlaybookRunStep> Steps);

public sealed record OperationsPlaybookDashboard(
    int TotalPlaybooks,
    int EnabledPlaybooks,
    int SuccessfulRuns,
    int FailedRuns,
    IReadOnlyList<OperationsPlaybook> Playbooks,
    IReadOnlyList<OperationsPlaybookRun> RecentRuns,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset GeneratedAt);

public sealed record OperationsPlaybookWriteRequest(string Name, string? Description, bool Enabled, IReadOnlyList<OperationsPlaybookStep> Steps);
public sealed record OperationsPlaybookRunRequest(string? Confirmation);

public sealed record SecurityPolicy(
    bool ApiTokensEnabled,
    int MaximumTokenDays,
    bool RequireHighRiskConfirmation,
    bool AuditTokenUse,
    int MaximumActiveTokens,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public static SecurityPolicy Default() => new(true, 90, true, true, 25, DateTimeOffset.MinValue, string.Empty);
}

public sealed record ApiTokenRecord(
    string Id,
    string Name,
    string Prefix,
    string TokenHash,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string CreatedBy,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    string RevokedBy)
{
    public bool Verify(string plainText, DateTimeOffset now, string requiredScope)
    {
        if (RevokedAt.HasValue || ExpiresAt <= now) return false;
        if (!Scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase)) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        byte[] expected;
        try { expected = Convert.FromHexString(TokenHash); }
        catch (FormatException) { return false; }
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed record ApiTokenView(
    string Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string CreatedBy,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    string RevokedBy);

public sealed record ApiTokenCreationResult(ApiTokenView Token, string PlainText);
public sealed record ApiTokenSecret(ApiTokenRecord Record, string PlainText)
{
    public static ApiTokenSecret Create(string scope, DateTimeOffset expiresAt, string name = "Integration token", string createdBy = "system")
    {
        var random = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
        var plainText = $"palops_{prefix}_{secret}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText))).ToLowerInvariant();
        var record = new ApiTokenRecord(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(name) ? "Integration token" : name.Trim(),
            prefix,
            hash,
            [scope.Trim().ToLowerInvariant()],
            DateTimeOffset.UtcNow,
            expiresAt,
            createdBy,
            null,
            null,
            string.Empty);
        return new(record, plainText);
    }
}

public sealed record SecurityObservation(
    string Id,
    string ObservationType,
    string Actor,
    string RemoteIp,
    string Outcome,
    string Message,
    DateTimeOffset OccurredAt);

public sealed record SecurityCenterDashboard(
    SecurityPolicy Policy,
    int ActiveTokens,
    int ExpiringTokens,
    int RevokedTokens,
    IReadOnlyList<ApiTokenView> Tokens,
    IReadOnlyList<SecurityObservation> RecentObservations,
    DateTimeOffset GeneratedAt);

public sealed record SecurityPolicyWriteRequest(bool ApiTokensEnabled, int MaximumTokenDays, bool RequireHighRiskConfirmation, bool AuditTokenUse, int MaximumActiveTokens);
public sealed record ApiTokenCreateRequest(string Name, IReadOnlyList<string> Scopes, int ExpiresInDays);
public sealed record ApiTokenRevokeRequest(string Confirmation);

public sealed record IntegrationSubscription(
    string Id,
    string Name,
    bool Enabled,
    IReadOnlyList<string> EventTypes,
    string Destination,
    string SigningSecretReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string LastStatus,
    string LastMessage,
    DateTimeOffset? LastDeliveredAt,
    string? DeliveryChannelId);

public sealed record IntegrationEventRecord(
    string Id,
    string EventType,
    string IdempotencyKey,
    string Source,
    string Outcome,
    string Message,
    DateTimeOffset ReceivedAt);

public sealed record IntegrationCenterDashboard(
    int EnabledSubscriptions,
    int TotalSubscriptions,
    int AcceptedEvents,
    int RejectedEvents,
    IReadOnlyList<IntegrationSubscription> Subscriptions,
    IReadOnlyList<IntegrationEventRecord> RecentEvents,
    DateTimeOffset GeneratedAt);

public sealed record IntegrationSubscriptionWriteRequest(string Name, bool Enabled, IReadOnlyList<string> EventTypes, string Destination, string? SigningSecretReference);
public sealed record IntegrationEventRequest(string EventType, string IdempotencyKey, string Source, IReadOnlyDictionary<string, string>? Metadata);

public sealed class StoredConfigurationVersion
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AdvancedOperationsStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<IncidentRecord> Incidents { get; set; } = [];
    public List<IncidentRule> IncidentRules { get; set; } = [];
    public Dictionary<string, PlayerInsightNote> PlayerNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GovernanceReview> GovernanceReviews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DisasterRecoveryTarget> DisasterRecoveryTargets { get; set; } = [];
    public List<DisasterRecoveryDrill> DisasterRecoveryDrills { get; set; } = [];
    public List<UpdatePlanRecord> UpdatePlans { get; set; } = [];
    public List<StoredConfigurationVersion> ConfigurationVersions { get; set; } = [];
    public List<OperationsPlaybook> Playbooks { get; set; } = [];
    public List<OperationsPlaybookRun> PlaybookRuns { get; set; } = [];
    public SecurityPolicy SecurityPolicy { get; set; } = SecurityPolicy.Default();
    public List<ApiTokenRecord> ApiTokens { get; set; } = [];
    public List<SecurityObservation> SecurityObservations { get; set; } = [];
    public List<IntegrationSubscription> IntegrationSubscriptions { get; set; } = [];
    public List<IntegrationEventRecord> IntegrationEvents { get; set; } = [];
}

public static class IncidentTransitions
{
    public static IncidentRecord Create(string title, string severity, string source, string fingerprint, DateTimeOffset now, string description = "", string assignee = "")
    {
        var timeline = new[] { Entry("created", "system", "Incident created.", now) };
        return new(
            Guid.NewGuid().ToString("N"), title.Trim(), description.Trim(), severity, IncidentStatus.Open,
            string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim(), fingerprint.Trim(), assignee.Trim(),
            now, now, null, 1, now, timeline);
    }

    public static IncidentRecord Acknowledge(IncidentRecord incident, string actor, string message, DateTimeOffset now)
    {
        if (incident.Status == IncidentStatus.Resolved) throw new InvalidOperationException("Resolved incidents must be reopened before acknowledgement.");
        return Append(incident with { Status = IncidentStatus.Acknowledged, UpdatedAt = now }, "acknowledged", actor, message, now);
    }

    public static IncidentRecord Assign(IncidentRecord incident, string actor, string assignee, DateTimeOffset now) =>
        Append(incident with { Assignee = assignee.Trim(), UpdatedAt = now }, "assigned", actor, $"Assigned to {assignee.Trim()}.", now);

    public static IncidentRecord AddComment(IncidentRecord incident, string actor, string message, DateTimeOffset now) =>
        Append(incident with { UpdatedAt = now }, "commented", actor, message, now);

    public static IncidentRecord Resolve(IncidentRecord incident, string actor, string message, DateTimeOffset now) =>
        Append(incident with { Status = IncidentStatus.Resolved, ResolvedAt = now, UpdatedAt = now }, "resolved", actor, message, now);

    public static IncidentRecord Reopen(IncidentRecord incident, string actor, string message, DateTimeOffset now) =>
        Append(incident with { Status = IncidentStatus.Open, ResolvedAt = null, UpdatedAt = now, LastObservedAt = now }, "reopened", actor, message, now);

    public static IncidentRecord ObserveAgain(IncidentRecord incident, string message, DateTimeOffset now) =>
        Append(incident with { OccurrenceCount = incident.OccurrenceCount + 1, LastObservedAt = now, UpdatedAt = now }, "observed", "system", message, now);

    private static IncidentRecord Append(IncidentRecord incident, string action, string actor, string message, DateTimeOffset now) =>
        incident with { Timeline = incident.Timeline.Append(Entry(action, actor, message, now)).TakeLast(500).ToArray() };

    private static IncidentTimelineEntry Entry(string action, string actor, string message, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N"), action, string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(), string.IsNullOrWhiteSpace(message) ? action : message.Trim(), now);
}

public static class PlayerRiskScorer
{
    public static int Score(int violationCount, bool banned, bool repeatedIdentity, bool suspiciousActivity)
    {
        var score = Math.Max(0, violationCount) * 12;
        if (banned) score += 45;
        if (repeatedIdentity) score += 20;
        if (suspiciousActivity) score += 20;
        return Math.Clamp(score, 0, 100);
    }

    public static string Level(int score) => score switch
    {
        >= 70 => "high",
        >= 35 => "medium",
        >= 1 => "low",
        _ => "none"
    };
}

public static class OperationsPlaybookCatalog
{
    public static IReadOnlyList<string> AllowedActions { get; } =
    [
        "health-refresh",
        "backup-create",
        "save-index",
        "notification-event",
        "maintenance-run"
    ];

    public static bool IsAllowed(string action) => AllowedActions.Contains(action?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
