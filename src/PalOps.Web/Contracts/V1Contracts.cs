using PalOps.Web.SaveGames;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.Contracts;

public sealed record ApiWarning(string Code, string Message);
public sealed record ApiResponse<T>(T Data, string RequestId, IReadOnlyList<ApiWarning> Warnings)
{
    public bool Success => true;
    public string Message { get; init; } = "OK";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PagedData<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record SaveSettingsV1Response(
    string WorldDirectory,
    bool AutoIndex,
    int StableChecks,
    int StableCheckIntervalSeconds,
    int PollIntervalSeconds,
    int MaximumFileSizeMb);

public sealed record PlayerListItemV1(
    string PlayerUid,
    string UserId,
    string SteamId,
    string Name,
    int Level,
    bool Online,
    string GuildName,
    string GuildId,
    double? Ping,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? SnapshotAt,
    double? X,
    double? Y,
    double? Z,
    string IdentitySource,
    string OnlineSource,
    string PositionSource);

public sealed record SystemOverviewV1(
    string ApplicationStatus,
    bool SaveConfigured,
    SaveIndexStatus SaveIndex,
    string? CurrentSnapshotId,
    DateTimeOffset? CurrentSnapshotAt,
    int IndexedPlayers,
    int IndexedItems,
    int IndexedPals,
    int IndexedGuilds,
    int IndexedBases,
    int OnlinePlayers,
    IReadOnlyList<HealthComponentV1> Components,
    BackupSummaryV1 Backups,
    AutomationSummaryV1 Automation);

public sealed record GuildSummaryV1(
    string GuildId,
    string Name,
    string? LeaderPlayerUid,
    int Level,
    int MemberCount,
    int BaseCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset SnapshotAt);

public sealed record MapEntityV1(
    string Id,
    string Type,
    string Label,
    double WorldX,
    double WorldY,
    double WorldZ,
    double? MapX,
    double? MapY,
    string? MapLayer,
    string CoordinateSpace,
    string PlacementStatus,
    string PlacementConfidence,
    string PlacementReason,
    string Source,
    DateTimeOffset SourceAt,
    bool Stale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MapEntityPageV1(
    IReadOnlyList<MapEntityV1> Items,
    int Total,
    int Resolved,
    int Unresolved,
    DateTimeOffset GeneratedAt);

public sealed record PlayerSourceV1(
    string Mode,
    bool SaveAvailable,
    bool LiveAvailable,
    string? SnapshotId,
    DateTimeOffset? SnapshotAt,
    bool Stale,
    string Message);

public sealed record PlayerDetailV1(
    string PlayerUid,
    string UserId,
    string SteamId,
    string Name,
    string AccountName,
    string GuildId,
    string GuildName,
    int Level,
    long Experience,
    long Hp,
    long MaxHp,
    long ShieldHp,
    long ShieldMaxHp,
    double FullStomach,
    IReadOnlyDictionary<string, int> StatusPoints,
    bool Online,
    double? Ping,
    double? X,
    double? Y,
    double? Z,
    DateTimeOffset? LastSeenAt,
    string SourceFile,
    int ItemCount,
    int PalCount,
    PlayerSourceV1 Source);

public sealed record PlayerItemV1(
    string PlayerUid,
    string ContainerType,
    string ContainerLabel,
    int SlotIndex,
    string ItemId,
    string NameZh,
    string NameEn,
    string Category,
    string ImageUrl,
    int Quantity,
    int Quality,
    double? Durability,
    string? DynamicItemId,
    bool Recognized);

public sealed record NamedGameValueV1(string Id, string Name);

public sealed record PlayerPalV1(
    string InstanceId,
    string PlayerUid,
    string ContainerType,
    string ContainerLabel,
    int SlotIndex,
    string PalId,
    string NameZh,
    string NameEn,
    string Category,
    string ImageUrl,
    string Nickname,
    int Level,
    long Experience,
    string Gender,
    bool IsLucky,
    bool IsBoss,
    bool IsTower,
    long Hp,
    long MaxHp,
    int Melee,
    int Ranged,
    int Defense,
    int WorkSpeed,
    int Rank,
    IReadOnlyList<NamedGameValueV1> Passives,
    IReadOnlyList<NamedGameValueV1> Skills,
    bool Recognized);
