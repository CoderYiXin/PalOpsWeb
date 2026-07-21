namespace PalOps.Web.PlayerDiscipline;

public sealed record DisciplineWhitelistEntry(
    string UserId,
    string DisplayName,
    DateTimeOffset? AddedAt,
    string AddedBy,
    string Notes,
    bool Online,
    bool ExternalRecord);

public sealed record DisciplineBanEntry(
    string Identifier,
    string BanType,
    string DisplayName,
    string Reason,
    string Operator,
    DateTimeOffset? BannedAt,
    DateTimeOffset? ExpiresAt,
    bool Temporary,
    bool Expired,
    bool Online,
    string Status,
    string Notes,
    bool ExternalRecord,
    string? LastError);

public sealed record DisciplineOnlinePlayer(
    string UserId,
    string PlayerUid,
    string Name,
    string Ip,
    string GuildName,
    string Status,
    bool Whitelisted,
    bool Banned,
    int ViolationCount);

public sealed record DisciplineIdentity(
    string UserId,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> PlayerUids,
    IReadOnlyList<string> GuildNames,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string Status,
    bool Online,
    int ViolationCount,
    string Notes);

public sealed record DisciplineViolation(
    string Id,
    string UserId,
    string DisplayName,
    string Severity,
    string Category,
    string Summary,
    string Details,
    string Operator,
    DateTimeOffset CreatedAt);

public sealed record DisciplineKickEntry(
    string Id,
    string UserId,
    string DisplayName,
    string Reason,
    string Operator,
    DateTimeOffset KickedAt,
    string Source);

public sealed record DisciplineOperation(
    string Id,
    string Operation,
    string Target,
    string Outcome,
    string Operator,
    string Summary,
    DateTimeOffset Timestamp,
    string? Error);

public sealed record PlayerDisciplineDashboard(
    IReadOnlyList<DisciplineWhitelistEntry> WhitelistEntries,
    IReadOnlyList<DisciplineBanEntry> BanEntries,
    IReadOnlyList<DisciplineOnlinePlayer> OnlinePlayers,
    IReadOnlyList<DisciplineIdentity> Identities,
    IReadOnlyList<DisciplineViolation> Violations,
    IReadOnlyList<DisciplineKickEntry> KickEntries,
    IReadOnlyList<DisciplineOperation> RecentOperations,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt);

public sealed record WhitelistWriteRequest(string UserId, string? DisplayName, string? Notes);
public sealed record BanWriteRequest(string Identifier, string BanType, string? DisplayName, string Reason, DateTimeOffset? ExpiresAt, string? Notes);
public sealed record UnbanRequest(string Reason);
public sealed record ViolationWriteRequest(string UserId, string? DisplayName, string Severity, string Category, string Summary, string? Details);
public sealed record IdentityNotesWriteRequest(string Notes);
public sealed record DisciplineImportRequest(string Kind, string Format, string Content);
public sealed record DisciplineImportResult(int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);
public sealed record DisciplineExportFile(string FileName, string ContentType, byte[] Content);

public sealed class PlayerDisciplineState
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, WhitelistMetadata> Whitelist { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, BanMetadata> Bans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IdentityMetadata> Identities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ViolationMetadata> Violations { get; set; } = [];
    public List<KickMetadata> Kicks { get; set; } = [];
    public List<DisciplineOperation> Operations { get; set; } = [];
}

public sealed class WhitelistMetadata
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BanMetadata
{
    public string Identifier { get; set; } = string.Empty;
    public string BanType { get; set; } = "account";
    public string DisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public DateTimeOffset BannedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset? UnbannedAt { get; set; }
    public string? UnbannedBy { get; set; }
    public string? LastError { get; set; }
}

public sealed class IdentityMetadata
{
    public string UserId { get; set; } = string.Empty;
    public HashSet<string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IpAddresses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PlayerUids { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> GuildNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class KickMetadata
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public DateTimeOffset KickedAt { get; set; }
    public string Source { get; set; } = "palops";
}

public sealed class ViolationMetadata
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record PalDefenderAccessControlSnapshot(
    IReadOnlyList<string> WhitelistIdentifiers,
    IReadOnlyList<PalDefenderBanRecord> BanRecords,
    string WhitelistSha256,
    string BanlistSha256,
    IReadOnlyList<string> Warnings);

public sealed record PalDefenderBanRecord(string Identifier, string BanType, string? Reason, DateTimeOffset? BannedAt);
