using PalOps.Web.SaveGames;

namespace PalOps.Web.SaveGames.Index;

public sealed record IndexedPlayer(
    string PlayerUid,
    string UserId,
    string Name,
    int Level,
    string? GuildId,
    string? GuildName,
    string? AccountName,
    DateTimeOffset? LastSeenAt,
    double? X,
    double? Y,
    double? Z,
    long Experience = 0,
    long Hp = 0,
    long MaxHp = 0,
    long ShieldHp = 0,
    long ShieldMaxHp = 0,
    double FullStomach = 0,
    IReadOnlyDictionary<string, int>? StatusPoints = null,
    string SourceFile = "");

public sealed record IndexedItem(
    string PlayerUid,
    string ContainerType,
    int SlotIndex,
    string ItemId,
    int Quantity,
    int Quality = 0,
    double? Durability = null,
    string? DynamicItemId = null);

public sealed record IndexedPal(
    string InstanceId,
    string PlayerUid,
    string PalId,
    string Nickname,
    int Level,
    long Experience,
    string Gender,
    bool IsLucky,
    bool IsBoss,
    bool IsTower,
    string ContainerType,
    int SlotIndex,
    long Hp,
    long MaxHp,
    int Melee,
    int Ranged,
    int Defense,
    int WorkSpeed,
    int Rank,
    IReadOnlyList<string> Passives,
    IReadOnlyList<string> Skills);

public sealed record IndexedGuild(
    string GuildId,
    string Name,
    string? LeaderPlayerUid,
    int Level,
    IReadOnlyList<string> MemberPlayerUids,
    DateTimeOffset? LastActivityAt)
{
    public IReadOnlyList<string> BaseIds { get; init; } = [];
}

public sealed record IndexedBaseCamp(
    string BaseId,
    string GuildId,
    double? X,
    double? Y,
    double? Z,
    int WorkerCount,
    int MapObjectCount)
{
    public string AssociationType { get; init; } = "unresolved";
    public string PositionSource { get; init; } = "unresolved";
    public IReadOnlyList<string> RelatedPlayerUids { get; init; } = [];
    public string? AssociationReason { get; init; }
    public bool PositionResolved => X.HasValue && Y.HasValue && Z.HasValue;
}

public sealed record IndexedMapMarker(
    string Id,
    string Type,
    string Label,
    double X,
    double Y,
    double Z,
    string Source,
    DateTimeOffset SourceAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record SaveIndexSnapshot(
    string SnapshotId,
    string WorldId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ParsedAt,
    string LevelSha256,
    string SourceWorldDirectory,
    IReadOnlyList<IndexedPlayer> Players,
    IReadOnlyList<IndexedItem> Items,
    IReadOnlyList<IndexedPal> Pals,
    IReadOnlyList<IndexedGuild> Guilds,
    IReadOnlyList<IndexedBaseCamp> Bases,
    IReadOnlyList<IndexedMapMarker> MapMarkers,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public static SaveIndexSnapshot Empty(string snapshotId, string worldId, DateTimeOffset createdAt)
        => new(
            snapshotId,
            worldId,
            createdAt,
            createdAt,
            string.Empty,
            string.Empty,
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record SaveIndexFailure(
    string SnapshotId,
    string WorldId,
    DateTimeOffset FailedAt,
    string Error);

public sealed record SaveIndexRepositoryHistory(
    IReadOnlyList<SaveIndexHistoryEntry> Entries,
    IReadOnlyList<SaveIndexFailure> Failures);
