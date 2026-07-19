namespace PalOps.Web.SaveGames.Projection;

/// <summary>
/// Authoritative player profile stored in Level.sav. The corresponding
/// Players/&lt;uid&gt;.sav contains position, last-online time and container IDs,
/// but not the complete profile or inventory contents.
/// </summary>
public sealed record WorldPlayerProfile(
    string PlayerUid,
    string InstanceId,
    string Name,
    int Level,
    string? GuildId,
    long Experience,
    long Hp,
    long MaxHp,
    long ShieldHp,
    long ShieldMaxHp,
    double FullStomach,
    IReadOnlyDictionary<string, int> StatusPoints);

/// <summary>
/// One occupied slot decoded from ItemContainerSaveData in Level.sav.
/// </summary>
public sealed record WorldItemSlot(
    int SlotIndex,
    string ItemId,
    int Quantity,
    int Quality = 0,
    double? Durability = null,
    string? DynamicItemId = null);

/// <summary>
/// Authoritative Pal profile decoded from CharacterSaveParameterMap in Level.sav.
/// </summary>
public sealed record WorldPalProfile(
    string InstanceId,
    string? OwnerPlayerUid,
    string? ContainerId,
    int SlotIndex,
    string PalId,
    string Nickname,
    int Level,
    long Experience,
    string Gender,
    bool IsLucky,
    bool IsBoss,
    bool IsTower,
    long Hp,
    long MaxHp,
    int HpTalent,
    int AttackTalent,
    int DefenseTalent,
    int WorkSpeed,
    int Rank,
    IReadOnlyList<string> Passives,
    IReadOnlyList<string> Skills);

/// <summary>
/// Stable prefix of a CharacterContainerSaveData slot. It provides a fallback
/// instance-to-container link for save versions that omit SlotId from a profile.
/// </summary>
public sealed record WorldCharacterSlot(
    string ContainerId,
    int SlotIndex,
    string? PlayerUid,
    string InstanceId);
