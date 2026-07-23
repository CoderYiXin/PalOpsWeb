namespace PalOps.Web.Contracts;

public sealed record ApiError(string Code, string Message, object? Details = null, string? Detail = null, string? SuggestedAction = null);
public sealed record ApiErrorEnvelope(ApiError Error, string? RequestId = null)
{
    public bool Success => false;
    public string Message => Error.Message;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LoginRequest(string Password, string? UserName = null);
public sealed record ReauthenticateRequest(string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record AuthStatusResponse(bool Authenticated, bool SetupRequired, string? UserName = null, string? Role = null, string? DisplayName = null);
public sealed record CsrfResponse(string Token);

public sealed record ServerSettingsUpdateRequest(
    string PalworldBaseUrl,
    string PalworldUserName,
    string? PalworldPassword,
    string PalDefenderBaseUrl,
    string? PalDefenderToken,
    string RconHost,
    int RconPort,
    string? RconPassword,
    bool RconBase64,
    int RconTimeoutSeconds,
    string SaveWorldDirectory,
    bool SaveAutoIndex,
    int SaveStableChecks,
    int SaveStableCheckIntervalSeconds,
    int SavePollIntervalSeconds,
    int SaveMaximumFileSizeMb,
    string BackupDirectory,
    int BackupRetentionCount,
    int BackupCompressionLevel,
    bool BackupExecuteSaveFirst,
    bool BackupRestoreEnabled,
    bool AutomationEnabled,
    int AutomationPollIntervalSeconds,
    int AutomationMaximumHistoryEntries);

public sealed record ServerSettingsSummaryResponse(
    string PalworldBaseUrl,
    string PalworldUserName,
    bool PalworldPasswordConfigured,
    string PalDefenderBaseUrl,
    bool PalDefenderTokenConfigured,
    string RconHost,
    int RconPort,
    bool RconPasswordConfigured,
    bool RconBase64,
    int RconTimeoutSeconds,
    string SaveWorldDirectory,
    bool SaveAutoIndex,
    int SaveStableChecks,
    int SaveStableCheckIntervalSeconds,
    int SavePollIntervalSeconds,
    int SaveMaximumFileSizeMb,
    string BackupDirectory,
    int BackupRetentionCount,
    int BackupCompressionLevel,
    bool BackupExecuteSaveFirst,
    bool BackupRestoreEnabled,
    bool AutomationEnabled,
    int AutomationPollIntervalSeconds,
    int AutomationMaximumHistoryEntries);

public sealed record ConnectionTestRequest(string Target, ServerSettingsUpdateRequest? Settings = null);
public sealed record ConnectionTestResponse(bool Success, string Message, object? Details = null);

public sealed record PlayerResponse(
    string Name,
    string UserId,
    string PlayerUid,
    string AccountName,
    string GuildName,
    int? Level,
    double? Ping,
    double? LocationX,
    double? LocationY,
    double? LocationZ,
    bool Online,
    string Source);

public sealed record ItemGrantRequest(string ItemId, int Count, bool Custom = false);
public sealed record PalGrantRequest(string PalId, int Level, int Count = 1, bool Custom = false);
public sealed record ProgressionGrantRequest(
    long? Experience,
    int? TechnologyPoints,
    int? AncientTechnologyPoints,
    IReadOnlyDictionary<string, int>? Relics);

public sealed record BulkGrantRequest(
    IReadOnlyList<string> PlayerIdentifiers,
    IReadOnlyList<ItemGrantRequest> Items,
    IReadOnlyList<PalGrantRequest> Pals,
    ProgressionGrantRequest? Progression);

public sealed record PlayerGrantResult(string PlayerIdentifier, bool Success, string Code, string Message);
public sealed record BulkGrantResponse(int RequestedPlayers, int SucceededPlayers, int FailedPlayers, IReadOnlyList<PlayerGrantResult> Results);

public sealed record CatalogEntryResponse(
    string Id,
    string Type,
    string NameZh,
    string NameEn,
    string Category,
    IReadOnlyList<string> Aliases,
    string ImageUrl,
    bool Favorite,
    DateTimeOffset? LastUsedAt,
    string Source);

public sealed record CatalogSearchResponse(IReadOnlyList<CatalogEntryResponse> Entries, int Total);
public sealed record CatalogCategoryResponse(string Category, int Count);
public sealed record CatalogFavoriteRequest(bool Favorite);
public sealed record CatalogAliasRequest(IReadOnlyList<string> Aliases);
public sealed record CatalogImportResponse(int Imported, int Replaced, int Rejected, IReadOnlyList<string> Errors);

public sealed record BroadcastRequest(string Message, bool Alert = false);
public sealed record PlayerMessageRequest(IReadOnlyList<string> PlayerIdentifiers, string SendType, string Message);
public sealed record KickRequest(string PlayerIdentifier, string? Reason);
public sealed record TimeRequest(string Hour);
public sealed record PlayerTargetRequest(string PlayerIdentifier);

public sealed record PlayerQuickActionRequest(
    IReadOnlyList<string> PlayerIdentifiers,
    string Action,
    string? TargetPlayerIdentifier,
    double? X,
    double? Y,
    double? Z,
    long? Amount,
    string? TechId);

public sealed record PlayerQuickActionResult(
    string PlayerIdentifier,
    bool Success,
    string Code,
    string Message,
    string Response,
    long ElapsedMilliseconds);

public sealed record PlayerQuickActionResponse(
    string Action,
    int RequestedPlayers,
    int SucceededPlayers,
    int FailedPlayers,
    IReadOnlyList<PlayerQuickActionResult> Results);

public sealed record RconExecuteRequest(
    string Command,
    bool ConfirmHighRisk,
    string? Reason,
    string? Password = null);
public sealed record RconExecuteResponse(
    bool Success,
    string Risk,
    string Response,
    long ElapsedMilliseconds,
    string Code = "OK",
    string Message = "");

public sealed record AuditEntryResponse(
    DateTimeOffset Timestamp,
    string EventType,
    string Outcome,
    string RemoteIp,
    string Summary,
    object? Data);
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Entries, int Page, int PageSize, bool HasMore);
