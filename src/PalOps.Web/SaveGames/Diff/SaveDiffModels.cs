using System.Text.Json.Serialization;

namespace PalOps.Web.SaveGames.Diff;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SaveDiffChangeKind
{
    Added,
    Removed,
    Changed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SaveDiffSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record SaveChangePlayer(
    string PlayerUid,
    string UserId,
    string Name,
    int Level,
    long Experience,
    string GuildId,
    double? X,
    double? Y,
    double? Z);

public sealed record SaveChangeGuild(
    string GuildId,
    string Name,
    int Level,
    string LeaderPlayerUid,
    IReadOnlyList<string> MemberPlayerUids);

public sealed record SaveChangeBase(
    string BaseId,
    string GuildId,
    double? X,
    double? Y,
    double? Z,
    int WorkerCount,
    int MapObjectCount,
    string AssociationType);

public sealed record SaveChangeItem(
    string PlayerUid,
    string ContainerType,
    string ItemId,
    int Quality,
    string DynamicItemId,
    int Quantity,
    bool Important);

public sealed record SaveChangePal(
    string PlayerUid,
    string PalId,
    int Count,
    int LuckyCount,
    int BossCount,
    double AverageLevel);

public sealed record SaveChangeSnapshot(
    int SchemaVersion,
    string SnapshotId,
    string WorldId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ParsedAt,
    string LevelSha256,
    IReadOnlyList<SaveChangePlayer> Players,
    IReadOnlyList<SaveChangeGuild> Guilds,
    IReadOnlyList<SaveChangeBase> Bases,
    IReadOnlyList<SaveChangeItem> Items,
    IReadOnlyList<SaveChangePal> Pals);

public sealed record SaveDiffSnapshotSummary(
    string SnapshotId,
    string WorldId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ParsedAt,
    string LevelSha256,
    int PlayerCount,
    int GuildCount,
    int BaseCount,
    int ItemEntryCount,
    long ItemQuantity,
    int PalSpeciesEntries,
    int PalCount);

public sealed record SaveDiffPlayerChange(
    SaveDiffChangeKind ChangeType,
    string PlayerUid,
    string BeforeName,
    string AfterName,
    int? BeforeLevel,
    int? AfterLevel,
    int LevelDelta,
    long? BeforeExperience,
    long? AfterExperience,
    long ExperienceDelta,
    string BeforeGuildId,
    string AfterGuildId,
    double? DistanceMoved,
    IReadOnlyList<string> ChangedFields);

public sealed record SaveDiffGuildChange(
    SaveDiffChangeKind ChangeType,
    string GuildId,
    string BeforeName,
    string AfterName,
    int? BeforeLevel,
    int? AfterLevel,
    string BeforeLeaderPlayerUid,
    string AfterLeaderPlayerUid,
    int BeforeMemberCount,
    int AfterMemberCount,
    IReadOnlyList<string> AddedMemberPlayerUids,
    IReadOnlyList<string> RemovedMemberPlayerUids,
    IReadOnlyList<string> ChangedFields);

public sealed record SaveDiffBaseChange(
    SaveDiffChangeKind ChangeType,
    string BaseId,
    string BeforeGuildId,
    string AfterGuildId,
    double? BeforeX,
    double? BeforeY,
    double? BeforeZ,
    double? AfterX,
    double? AfterY,
    double? AfterZ,
    double? DistanceMoved,
    int? BeforeWorkerCount,
    int? AfterWorkerCount,
    int? BeforeMapObjectCount,
    int? AfterMapObjectCount,
    IReadOnlyList<string> ChangedFields);

public sealed record SaveDiffItemChange(
    SaveDiffChangeKind ChangeType,
    string PlayerUid,
    string PlayerName,
    string ContainerType,
    string ItemId,
    string ItemName,
    int Quality,
    string DynamicItemId,
    int BeforeQuantity,
    int AfterQuantity,
    int QuantityDelta,
    bool Important);

public sealed record SaveDiffPalChange(
    SaveDiffChangeKind ChangeType,
    string PlayerUid,
    string PlayerName,
    string PalId,
    string PalName,
    int BeforeCount,
    int AfterCount,
    int CountDelta,
    int BeforeLuckyCount,
    int AfterLuckyCount,
    int BeforeBossCount,
    int AfterBossCount,
    double BeforeAverageLevel,
    double AfterAverageLevel);

public sealed record SaveDiffAnomaly(
    SaveDiffSeverity Severity,
    string Rule,
    string Category,
    string EntityId,
    string Title,
    string Description,
    double? BeforeValue,
    double? AfterValue,
    double? ChangePercent);

public sealed record SaveDiffCollection<T>(
    IReadOnlyList<T> Items,
    int Total,
    bool Truncated);

public sealed record SaveDiffSummary(
    int AddedPlayers,
    int RemovedPlayers,
    int ChangedPlayers,
    int ChangedGuilds,
    int ChangedBases,
    int ChangedItems,
    int ImportantItemChanges,
    int ChangedPals,
    int AnomalyCount,
    int CriticalAnomalyCount);

public sealed record SaveDiffReport(
    SaveDiffSnapshotSummary From,
    SaveDiffSnapshotSummary To,
    DateTimeOffset GeneratedAt,
    SaveDiffSummary Summary,
    SaveDiffCollection<SaveDiffPlayerChange> PlayerChanges,
    SaveDiffCollection<SaveDiffGuildChange> GuildChanges,
    SaveDiffCollection<SaveDiffBaseChange> BaseChanges,
    SaveDiffCollection<SaveDiffItemChange> ItemChanges,
    SaveDiffCollection<SaveDiffPalChange> PalChanges,
    IReadOnlyList<SaveDiffAnomaly> Anomalies);

public sealed class SaveDiffValidationException : Exception
{
    public SaveDiffValidationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
