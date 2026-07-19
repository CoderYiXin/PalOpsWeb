using System.Globalization;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.SaveGames.RawData;

namespace PalOps.Web.SaveGames.Projection;

public sealed record WorldSaveProjection(
    IReadOnlyList<IndexedGuild> Guilds,
    IReadOnlyList<IndexedBaseCamp> Bases,
    IReadOnlyList<IndexedMapMarker> Markers,
    IReadOnlyDictionary<string, WorldPlayerProfile> PlayerProfiles,
    IReadOnlyDictionary<string, IReadOnlyList<WorldItemSlot>> ItemContainers,
    IReadOnlyDictionary<string, WorldPalProfile> PalProfiles,
    IReadOnlyDictionary<string, IReadOnlyList<WorldCharacterSlot>> CharacterContainers,
    IReadOnlyDictionary<string, string> Diagnostics);

public interface IWorldSaveProjector
{
    WorldSaveProjection Project(GvasDocument document, DateTimeOffset sourceAt);
}

/// <summary>
/// Projects the authoritative Level.sav domains. Player save files only provide
/// identity, position, last-online time and container references; player profiles,
/// item slots and Pal profiles are stored in the world save and must be joined by ID.
/// </summary>
public sealed class WorldSaveProjector(IPalworldRawDataDecoder rawData, IGuildBaseReconciliationService baseReconciliation) : IWorldSaveProjector
{
    private readonly GvasValueNavigator _navigator = new(maximumNodes: 5_000_000);

    public WorldSaveProjection Project(GvasDocument document, DateTimeOffset sourceAt)
    {
        ArgumentNullException.ThrowIfNull(document);

        var worldProperties = GetStruct(document.Properties, "worldSaveData") ?? document.Properties;
        var playerProfiles = new Dictionary<string, WorldPlayerProfile>(StringComparer.OrdinalIgnoreCase);
        var itemContainers = new Dictionary<string, IReadOnlyList<WorldItemSlot>>(StringComparer.OrdinalIgnoreCase);
        var palProfiles = new Dictionary<string, WorldPalProfile>(StringComparer.OrdinalIgnoreCase);
        var characterContainers = new Dictionary<string, IReadOnlyList<WorldCharacterSlot>>(StringComparer.OrdinalIgnoreCase);
        var guilds = new Dictionary<string, IndexedGuild>(StringComparer.OrdinalIgnoreCase);
        var bases = new Dictionary<string, IndexedBaseCamp>(StringComparer.OrdinalIgnoreCase);
        var markers = new Dictionary<string, IndexedMapMarker>(StringComparer.OrdinalIgnoreCase);

        var characterMapEntries = 0;
        var characterRawDecoded = 0;
        var characterRawRejected = 0;
        var characterRawTrailingBytes = 0;
        var itemMapEntries = 0;
        var itemSlotsDecoded = 0;
        var itemSlotsRejected = 0;
        var itemSlotTrailingBytes = 0;
        var characterContainerEntries = 0;
        var characterContainerSlotsDecoded = 0;
        var characterContainerSlotsRejected = 0;
        var characterContainerTrailingBytes = 0;
        var rawCandidates = 0;
        var decodedGroups = 0;
        var groupTrailingBytes = 0;
        var groupMapEntries = 0;
        var baseMapEntries = 0;
        var unresolvedBasePositions = 0;

        ProjectCharacters(
            ReadMap(worldProperties, "CharacterSaveParameterMap"),
            playerProfiles,
            palProfiles,
            ref characterMapEntries,
            ref characterRawDecoded,
            ref characterRawRejected,
            ref characterRawTrailingBytes);

        ProjectItemContainers(
            ReadMap(worldProperties, "ItemContainerSaveData"),
            itemContainers,
            ref itemMapEntries,
            ref itemSlotsDecoded,
            ref itemSlotsRejected,
            ref itemSlotTrailingBytes);

        ProjectCharacterContainers(
            ReadMap(worldProperties, "CharacterContainerSaveData"),
            characterContainers,
            ref characterContainerEntries,
            ref characterContainerSlotsDecoded,
            ref characterContainerSlotsRejected,
            ref characterContainerTrailingBytes);

        ValidateAuthoritativeDomains(
            characterMapEntries,
            characterRawDecoded,
            itemMapEntries,
            itemSlotsDecoded,
            itemSlotsRejected,
            characterContainerEntries,
            characterContainerSlotsDecoded,
            characterContainerSlotsRejected);

        var groupMap = ReadMap(worldProperties, "GroupSaveDataMap");
        groupMapEntries = groupMap.Count;
        foreach (var entry in groupMap)
        {
            if (!TryGetProperties(entry.Value, out var properties)) continue;
            ProcessGuildCandidate(
                properties,
                NormalizeId(entry.Key),
                guilds,
                ref rawCandidates,
                ref decodedGroups,
                ref groupTrailingBytes);
        }

        // Character records always carry their owning group ID. This remains a
        // reliable membership fallback when a newer guild RawData layout cannot be
        // decoded yet. Existing decoded guild names and base IDs are retained.
        ReconcileGuildMembership(guilds, playerProfiles.Values);

        var objectPositions = BuildMapObjectPositions(ReadMap(worldProperties, "MapObjectSaveData"));
        var candidates = new List<BaseCampProjectionCandidate>();
        var baseMap = ReadMap(worldProperties, "BaseCampSaveData");
        baseMapEntries = baseMap.Count;
        foreach (var entry in baseMap)
        {
            if (!TryGetProperties(entry.Value, out var properties)) continue;
            var baseId = NormalizeId(entry.Key)
                         ?? NormalizeId(ReadValue(properties, "BaseId", "BaseID", "BaseCampId", "BaseCampID"));
            if (string.IsNullOrWhiteSpace(baseId)) continue;
            candidates.Add(CreateBaseCandidate(properties, baseId, objectPositions));
        }

        foreach (var reconciled in baseReconciliation.Reconcile(candidates, guilds, playerProfiles, sourceAt))
        {
            bases[reconciled.BaseCamp.BaseId] = reconciled.BaseCamp;
            if (reconciled.Marker is not null) markers[reconciled.Marker.Id] = reconciled.Marker;
        }
        unresolvedBasePositions = bases.Values.Count(baseCamp => !baseCamp.PositionResolved);

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["saveGameClass"] = document.SaveGameClassName,
            ["topLevelProperties"] = document.Properties.Count.ToString(CultureInfo.InvariantCulture),
            ["characterMapEntries"] = characterMapEntries.ToString(CultureInfo.InvariantCulture),
            ["characterRawDataDecoded"] = characterRawDecoded.ToString(CultureInfo.InvariantCulture),
            ["characterRawDataRejected"] = characterRawRejected.ToString(CultureInfo.InvariantCulture),
            ["characterRawDataTrailingBytes"] = characterRawTrailingBytes.ToString(CultureInfo.InvariantCulture),
            ["worldPlayerProfiles"] = playerProfiles.Count.ToString(CultureInfo.InvariantCulture),
            ["worldPalProfiles"] = palProfiles.Count.ToString(CultureInfo.InvariantCulture),
            ["itemContainerMapEntries"] = itemMapEntries.ToString(CultureInfo.InvariantCulture),
            ["itemContainersDecoded"] = itemContainers.Count.ToString(CultureInfo.InvariantCulture),
            ["itemSlotsDecoded"] = itemSlotsDecoded.ToString(CultureInfo.InvariantCulture),
            ["itemSlotsRejected"] = itemSlotsRejected.ToString(CultureInfo.InvariantCulture),
            ["itemSlotTrailingBytes"] = itemSlotTrailingBytes.ToString(CultureInfo.InvariantCulture),
            ["characterContainerMapEntries"] = characterContainerEntries.ToString(CultureInfo.InvariantCulture),
            ["characterContainerSlotsDecoded"] = characterContainerSlotsDecoded.ToString(CultureInfo.InvariantCulture),
            ["characterContainerSlotsRejected"] = characterContainerSlotsRejected.ToString(CultureInfo.InvariantCulture),
            ["characterContainerTrailingBytes"] = characterContainerTrailingBytes.ToString(CultureInfo.InvariantCulture),
            ["groupMapEntries"] = groupMapEntries.ToString(CultureInfo.InvariantCulture),
            ["groupRawDataCandidates"] = rawCandidates.ToString(CultureInfo.InvariantCulture),
            ["groupRawDataDecoded"] = decodedGroups.ToString(CultureInfo.InvariantCulture),
            ["groupRawDataTrailingBytes"] = groupTrailingBytes.ToString(CultureInfo.InvariantCulture),
            ["baseMapEntries"] = baseMapEntries.ToString(CultureInfo.InvariantCulture),
            ["baseCandidates"] = baseMapEntries.ToString(CultureInfo.InvariantCulture),
            ["baseDirectPositions"] = bases.Values.Count(baseCamp => baseCamp.PositionSource == "direct").ToString(CultureInfo.InvariantCulture),
            ["baseLinkedObjectPositions"] = bases.Values.Count(baseCamp => baseCamp.PositionSource == "object-link").ToString(CultureInfo.InvariantCulture),
            ["baseUnresolvedPositions"] = unresolvedBasePositions.ToString(CultureInfo.InvariantCulture),
            ["baseDirectAssociations"] = bases.Values.Count(baseCamp => baseCamp.AssociationType == "direct").ToString(CultureInfo.InvariantCulture),
            ["baseInferredAssociations"] = bases.Values.Count(baseCamp => baseCamp.AssociationType is "guild-list" or "member" or "object-link").ToString(CultureInfo.InvariantCulture),
            ["baseUnresolvedAssociations"] = bases.Values.Count(baseCamp => baseCamp.AssociationType == "unresolved").ToString(CultureInfo.InvariantCulture),
            ["indexedGuilds"] = guilds.Count.ToString(CultureInfo.InvariantCulture),
            ["indexedBases"] = bases.Count.ToString(CultureInfo.InvariantCulture)
        };

        return new WorldSaveProjection(
            guilds.Values.OrderBy(guild => guild.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            bases.Values.ToArray(),
            markers.Values.ToArray(),
            playerProfiles,
            itemContainers,
            palProfiles,
            characterContainers,
            diagnostics);
    }

    private void ProjectCharacters(
        IReadOnlyList<GvasMapEntry> map,
        IDictionary<string, WorldPlayerProfile> playerProfiles,
        IDictionary<string, WorldPalProfile> palProfiles,
        ref int mapEntries,
        ref int decodedCount,
        ref int rejectedCount,
        ref int trailingBytes)
    {
        mapEntries = map.Count;
        foreach (var entry in map)
        {
            if (!TryGetProperties(entry.Key, out var keyProperties)
                || !TryGetProperties(entry.Value, out var valueProperties)
                || !valueProperties.TryGetValue("RawData", out var rawProperty))
            {
                rejectedCount++;
                continue;
            }

            if (!rawData.TryDecodeCharacter(rawProperty.Value, out var decoded)
                || GetStruct(decoded.Properties, "SaveParameter") is not { } saveParameter)
            {
                rejectedCount++;
                continue;
            }

            decodedCount++;
            trailingBytes += decoded.TrailingBytes;

            var playerUid = NormalizeId(ReadValue(keyProperties, "PlayerUId", "PlayerUid"));
            var instanceId = NormalizeId(ReadValue(keyProperties, "InstanceId", "InstanceID"));
            var groupId = decoded.GroupId == Guid.Empty ? null : NormalizeId(decoded.GroupId);

            if (ReadBoolean(saveParameter, "IsPlayer"))
            {
                if (string.IsNullOrWhiteSpace(playerUid))
                {
                    rejectedCount++;
                    continue;
                }

                var hp = ReadFixedPoint(saveParameter, "Hp", "HP");
                var shield = ReadFixedPoint(saveParameter, "ShieldHP", "ShieldHp");
                var profile = new WorldPlayerProfile(
                    playerUid,
                    instanceId ?? string.Empty,
                    FirstNonEmpty(ReadText(saveParameter, "NickName", "FilteredNickName"), playerUid),
                    ReadInt(saveParameter, "Level"),
                    groupId,
                    ReadLong(saveParameter, "Exp", "Experience"),
                    hp,
                    hp,
                    shield,
                    shield,
                    ReadDouble(saveParameter, "FullStomach") ?? 0,
                    ReadStatusPoints(saveParameter));
                playerProfiles[playerUid] = profile;
                continue;
            }

            var palId = ReadText(saveParameter, "CharacterID", "CharacterId");
            if (string.IsNullOrWhiteSpace(palId) || string.IsNullOrWhiteSpace(instanceId)) continue;

            var slotId = GetStruct(saveParameter, "SlotId", "SlotID");
            var containerId = slotId is null
                ? null
                : NormalizeId(ReadNestedValue(slotId, "ContainerId", "ID"));
            var slotIndex = slotId is null ? 0 : ReadInt(slotId, "SlotIndex");
            var hpValue = ReadFixedPoint(saveParameter, "Hp", "HP");
            var palStatusPoints = ReadStatusPoints(saveParameter);
            var gender = NormalizeEnum(ReadText(saveParameter, "Gender"));
            var passives = ReadTextArray(saveParameter, "PassiveSkillList")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var skills = ReadTextArray(saveParameter, "EquipWaza")
                .Concat(ReadTextArray(saveParameter, "MasteredWaza"))
                .Select(NormalizeEnum)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var directWorkSpeed = ReadInt(saveParameter, "CraftSpeed", "WorkSpeed");
            var workSpeed = directWorkSpeed != 0
                ? directWorkSpeed
                : palStatusPoints.TryGetValue("CraftSpeed", out var statusWork) ? statusWork : 0;

            palProfiles[instanceId] = new WorldPalProfile(
                instanceId,
                NormalizeId(ReadValue(saveParameter, "OwnerPlayerUId", "OwnerPlayerUid")),
                containerId,
                slotIndex,
                palId,
                ReadText(saveParameter, "NickName") ?? string.Empty,
                ReadInt(saveParameter, "Level"),
                ReadLong(saveParameter, "Exp", "Experience"),
                gender,
                ReadBoolean(saveParameter, "IsRarePal", "IsLucky"),
                // CharacterID prefixes describe the species variant, not the runtime
                // boss flag. Use the serialized IsBoss value to avoid marking normal
                // breedable BOSS_* variants as bosses.
                ReadBoolean(saveParameter, "IsBoss"),
                ReadBoolean(saveParameter, "IsTower"),
                hpValue,
                hpValue,
                ReadInt(saveParameter, "Talent_HP", "TalentHp"),
                ReadInt(saveParameter, "Talent_Shot", "TalentAttack", "Talent_Attack"),
                ReadInt(saveParameter, "Talent_Defense", "TalentDefense"),
                workSpeed,
                ReadInt(saveParameter, "Rank"),
                passives,
                skills);
        }
    }

    private void ProjectItemContainers(
        IReadOnlyList<GvasMapEntry> map,
        IDictionary<string, IReadOnlyList<WorldItemSlot>> containers,
        ref int mapEntries,
        ref int decodedSlots,
        ref int rejectedSlots,
        ref int trailingBytes)
    {
        mapEntries = map.Count;
        foreach (var entry in map)
        {
            if (!TryGetProperties(entry.Key, out var keyProperties)
                || !TryGetProperties(entry.Value, out var valueProperties))
                continue;

            var containerId = NormalizeId(ReadValue(keyProperties, "ID", "Id"));
            if (string.IsNullOrWhiteSpace(containerId)) continue;

            var slots = new List<WorldItemSlot>();
            foreach (var slotProperties in ReadStructArray(valueProperties, "Slots"))
            {
                if (!slotProperties.TryGetValue("RawData", out var rawProperty)
                    || !rawData.TryDecodeItemSlot(rawProperty.Value, out var decoded))
                {
                    rejectedSlots++;
                    continue;
                }

                decodedSlots++;
                trailingBytes += decoded.TrailingBytes;
                if (decoded.Quantity <= 0 || string.IsNullOrWhiteSpace(decoded.ItemId)) continue;
                slots.Add(new WorldItemSlot(
                    decoded.SlotIndex,
                    decoded.ItemId,
                    decoded.Quantity,
                    DynamicItemId: decoded.LocalDynamicId == Guid.Empty
                        ? null
                        : decoded.LocalDynamicId.ToString("N").ToUpperInvariant()));
            }

            containers[containerId] = slots
                .OrderBy(slot => slot.SlotIndex)
                .ToArray();
        }
    }

    private void ProjectCharacterContainers(
        IReadOnlyList<GvasMapEntry> map,
        IDictionary<string, IReadOnlyList<WorldCharacterSlot>> containers,
        ref int mapEntries,
        ref int decodedSlots,
        ref int rejectedSlots,
        ref int trailingBytes)
    {
        mapEntries = map.Count;
        foreach (var entry in map)
        {
            if (!TryGetProperties(entry.Key, out var keyProperties)
                || !TryGetProperties(entry.Value, out var valueProperties))
                continue;

            var containerId = NormalizeId(ReadValue(keyProperties, "ID", "Id"));
            if (string.IsNullOrWhiteSpace(containerId)) continue;

            var slots = new List<WorldCharacterSlot>();
            foreach (var slotProperties in ReadStructArray(valueProperties, "Slots"))
            {
                if (!slotProperties.TryGetValue("RawData", out var rawProperty)
                    || !rawData.TryDecodeCharacterContainer(rawProperty.Value, out var decoded))
                {
                    rejectedSlots++;
                    continue;
                }

                decodedSlots++;
                trailingBytes += decoded.TrailingBytes;
                var instanceId = NormalizeId(decoded.InstanceId);
                if (string.IsNullOrWhiteSpace(instanceId)) continue;

                slots.Add(new WorldCharacterSlot(
                    containerId,
                    ReadInt(slotProperties, "SlotIndex"),
                    decoded.PlayerUid == Guid.Empty ? null : NormalizeId(decoded.PlayerUid),
                    instanceId));
            }

            containers[containerId] = slots.ToArray();
        }
    }

    private static void ValidateAuthoritativeDomains(
        int characterMapEntries,
        int characterRawDecoded,
        int itemMapEntries,
        int itemSlotsDecoded,
        int itemSlotsRejected,
        int characterContainerEntries,
        int characterContainerSlotsDecoded,
        int characterContainerSlotsRejected)
    {
        if (characterMapEntries > 0 && characterRawDecoded == 0)
            throw new InvalidDataException(
                "CharacterSaveParameterMap 存在数据，但角色 RawData 全部解码失败；拒绝发布空玩家/帕鲁索引。");

        if (itemMapEntries > 0 && itemSlotsRejected > 0 && itemSlotsDecoded == 0)
            throw new InvalidDataException(
                "ItemContainerSaveData 存在数据，但物品容器全部投影失败；拒绝发布空背包索引。");

        if (characterContainerEntries > 0
            && characterContainerSlotsRejected > 0
            && characterContainerSlotsDecoded == 0)
            throw new InvalidDataException(
                "CharacterContainerSaveData 存在数据，但角色容器全部投影失败；拒绝发布空帕鲁容器索引。");
    }

    private void ProcessGuildCandidate(
        IReadOnlyDictionary<string, GvasProperty> properties,
        string? mapKey,
        IDictionary<string, IndexedGuild> guilds,
        ref int rawCandidates,
        ref int decodedGroups,
        ref int groupTrailingBytes)
    {
        var groupType = ReadText(properties, "GroupType", "Type");
        if (!string.IsNullOrWhiteSpace(groupType) && properties.TryGetValue("RawData", out var rawProperty))
        {
            rawCandidates++;
            if (rawData.TryDecodeGroup(rawProperty.Value, groupType, out var decoded))
            {
                decodedGroups++;
                groupTrailingBytes += decoded.TrailingBytes;
                if (groupType.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                {
                    var guildId = decoded.GroupId == Guid.Empty
                        ? NormalizeId(mapKey)
                        : NormalizeId(decoded.GroupId);
                    if (!string.IsNullOrWhiteSpace(guildId))
                    {
                        var lastActivity = decoded.Members
                            .Select(member => ConvertLastOnline(member.LastOnlineRaw))
                            .Where(value => value.HasValue)
                            .Select(value => value!.Value)
                            .DefaultIfEmpty()
                            .Max();
                        guilds[guildId] = new IndexedGuild(
                            guildId,
                            FirstNonEmpty(decoded.GuildName, decoded.GroupName, guildId),
                            decoded.AdminPlayerUid is { } adminPlayerUid && adminPlayerUid != Guid.Empty
                                ? adminPlayerUid.ToString("N").ToUpperInvariant()
                                : null,
                            decoded.BaseCampLevel,
                            decoded.Members
                                .Select(member => member.PlayerUid.ToString("N").ToUpperInvariant())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray(),
                            lastActivity == default ? null : lastActivity)
                        {
                            BaseIds = decoded.BaseIds
                                .Select(value => value.ToString("N").ToUpperInvariant())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                        };
                    }
                }
            }
        }

        var genericGuildId = NormalizeId(ReadValue(properties, "GuildId", "GuildID", "GroupId", "GroupID")) ?? mapKey;
        var guildName = ReadText(properties, "GuildName", "GroupName");
        if (string.IsNullOrWhiteSpace(genericGuildId) || string.IsNullOrWhiteSpace(guildName)) return;

        var members = ReadTextArray(properties, "MemberPlayerUids", "Players", "Members", "PlayerUids")
            .Select(NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var baseIds = ReadTextArray(properties, "BaseIds", "BaseCampIds", "BaseCampIdList")
            .Select(NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (guilds.TryGetValue(genericGuildId, out var existing))
        {
            var merged = existing with
            {
                Name = FirstNonEmpty(existing.Name, guildName, genericGuildId),
                LeaderPlayerUid = existing.LeaderPlayerUid
                                  ?? NormalizeId(ReadValue(properties, "LeaderPlayerUid", "AdminPlayerUid", "LeaderId")),
                Level = Math.Max(existing.Level, ReadInt(properties, "GuildLevel", "Level")),
                MemberPlayerUids = existing.MemberPlayerUids.Concat(members).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                LastActivityAt = existing.LastActivityAt ?? ReadDate(properties, "LastActivity", "LastOnline")
            };
            guilds[genericGuildId] = merged with
            {
                BaseIds = existing.BaseIds.Concat(baseIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            return;
        }

        guilds[genericGuildId] = new IndexedGuild(
            genericGuildId,
            guildName,
            NormalizeId(ReadValue(properties, "LeaderPlayerUid", "AdminPlayerUid", "LeaderId")),
            ReadInt(properties, "GuildLevel", "Level"),
            members,
            ReadDate(properties, "LastActivity", "LastOnline"))
        {
            BaseIds = baseIds
        };
    }

    private static void ReconcileGuildMembership(
        IDictionary<string, IndexedGuild> guilds,
        IEnumerable<WorldPlayerProfile> profiles)
    {
        foreach (var group in profiles
                     .Where(profile => !string.IsNullOrWhiteSpace(profile.GuildId))
                     .GroupBy(profile => profile.GuildId!, StringComparer.OrdinalIgnoreCase))
        {
            var members = group
                .Select(profile => profile.PlayerUid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (guilds.TryGetValue(group.Key, out var guild))
            {
                guilds[group.Key] = guild with
                {
                    MemberPlayerUids = guild.MemberPlayerUids
                        .Concat(members)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
                continue;
            }

            guilds[group.Key] = new IndexedGuild(
                group.Key,
                $"Guild {group.Key[..Math.Min(8, group.Key.Length)]}",
                null,
                0,
                members,
                null);
        }
    }

    private BaseCampProjectionCandidate CreateBaseCandidate(
        IReadOnlyDictionary<string, GvasProperty> properties,
        string baseId,
        IReadOnlyDictionary<string, WorldVector> objectPositions)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? directGuildId = null;
        foreach (var field in new[] { "GroupIdBelongTo", "OwnerGuildId", "OwnerGroupId", "GuildId", "GuildID", "GroupId", "GroupID" })
        {
            directGuildId = NormalizeId(ReadValue(properties, field));
            if (!string.IsNullOrWhiteSpace(directGuildId))
            {
                evidence["directGuildField"] = field;
                break;
            }
        }

        var relatedPlayers = new[]
            {
                NormalizeId(ReadValue(properties, "OwnerPlayerUid", "OwnerPlayerUId", "BuilderPlayerUid", "BuildPlayerUid"))
            }
            .Concat(ReadTextArray(properties, "WorkerPlayerUids", "WorkPlayerUids", "MemberPlayerUids").Select(NormalizeId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WorldVector? directPosition = null;
        var directPositionSource = "unresolved";
        var rawDataDecoded = false;
        var expectedBaseId = Guid.TryParseExact(baseId, "N", out var compactGuid)
            ? compactGuid
            : Guid.TryParse(baseId, out var parsedGuid) ? parsedGuid : Guid.Empty;
        if (rawData.TryDecodeBaseCamp(ReadValue(properties, "RawData"), expectedBaseId, out var rawBase))
        {
            directPosition = new WorldVector(rawBase.X, rawBase.Y, rawBase.Z);
            directPositionSource = "raw-data";
            rawDataDecoded = true;
            evidence["positionField"] = "RawData";
            evidence["rawDataDecoded"] = "true";
            evidence["rawDataTrailingBytes"] = rawBase.TrailingBytes.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(rawBase.Name)) evidence["baseName"] = rawBase.Name;
        }
        else
        {
            var directVector = ReadVectorDeep(properties, "Position", "Location", "BaseLocation", "Translation", "WorldLocation");
            directPosition = directVector is null ? null : new WorldVector(directVector.X, directVector.Y, directVector.Z);
            if (directPosition.HasValue)
            {
                directPositionSource = "direct";
                evidence["positionField"] = "direct";
            }
            evidence["rawDataDecoded"] = rawDataDecoded.ToString().ToLowerInvariant();
        }

        var objectId = NormalizeId(ReadValue(properties, "MapObjectId", "MapObjectID", "InstanceId", "InstanceID", "BaseCampModuleId"));
        WorldVector? linkedPosition = null;
        if (objectPositions.TryGetValue(baseId, out var baseLinked))
        {
            linkedPosition = baseLinked;
            evidence["linkedObjectId"] = baseId;
        }
        else if (!string.IsNullOrWhiteSpace(objectId) && objectPositions.TryGetValue(objectId, out var objectLinked))
        {
            linkedPosition = objectLinked;
            evidence["linkedObjectId"] = objectId;
        }

        return new BaseCampProjectionCandidate(
            baseId,
            directGuildId,
            relatedPlayers,
            directPosition,
            directPositionSource,
            linkedPosition,
            ReadIntDeep(properties, "WorkerCount", "WorkPalCount", "WorkCharacterCount"),
            ReadIntDeep(properties, "MapObjectCount", "BuildObjectCount", "ObjectCount"),
            evidence);
    }

    private Dictionary<string, WorldVector> BuildMapObjectPositions(IReadOnlyList<GvasMapEntry> mapObjects)
    {
        var result = new Dictionary<string, WorldVector>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mapObjects)
        {
            if (!TryGetProperties(entry.Value, out var properties)) continue;
            var vector = ReadVectorDeep(properties, "Position", "Location", "Translation", "WorldLocation");
            if (vector is null) continue;
            var position = new WorldVector(vector.X, vector.Y, vector.Z);
            foreach (var id in new[]
                     {
                         NormalizeId(entry.Key),
                         NormalizeId(ReadValue(properties, "BaseCampId", "BaseId", "OwnerBaseCampId")),
                         NormalizeId(ReadValue(properties, "MapObjectId", "InstanceId"))
                     }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
                result.TryAdd(id, position);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, GvasProperty>? GetStruct(
        IReadOnlyDictionary<string, GvasProperty> properties,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (properties.TryGetValue(name, out var property)
                && property.Value is IReadOnlyDictionary<string, GvasProperty> nested)
                return nested;
        }
        return null;
    }

    private static IReadOnlyList<GvasMapEntry> ReadMap(
        IReadOnlyDictionary<string, GvasProperty> properties,
        string name)
        => properties.TryGetValue(name, out var property)
           && property.Value is IReadOnlyList<GvasMapEntry> map
            ? map
            : [];

    private static IReadOnlyList<IReadOnlyDictionary<string, GvasProperty>> ReadStructArray(
        IReadOnlyDictionary<string, GvasProperty> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var property)
            || property.Value is not IReadOnlyList<object?> values)
            return [];

        return values
            .OfType<IReadOnlyDictionary<string, GvasProperty>>()
            .ToArray();
    }

    private static bool TryGetProperties(object? value, out IReadOnlyDictionary<string, GvasProperty> properties)
    {
        if (value is IReadOnlyDictionary<string, GvasProperty> direct)
        {
            properties = direct;
            return true;
        }
        if (value is GvasProperty property && property.Value is IReadOnlyDictionary<string, GvasProperty> nested)
        {
            properties = nested;
            return true;
        }
        properties = default!;
        return false;
    }

    private static object? ReadValue(
        IReadOnlyDictionary<string, GvasProperty> properties,
        params string[] names)
    {
        foreach (var name in names)
            if (properties.TryGetValue(name, out var property)) return property.Value;
        return null;
    }

    private static object? ReadNestedValue(
        IReadOnlyDictionary<string, GvasProperty> properties,
        params string[] path)
    {
        object? current = properties;
        foreach (var segment in path)
        {
            if (current is not IReadOnlyDictionary<string, GvasProperty> bag
                || !bag.TryGetValue(segment, out var property))
                return null;
            current = property.Value;
        }
        return current;
    }

    private static string? ReadText(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToText(ReadValue(properties, names));

    private static int ReadInt(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToInt32(ReadValue(properties, names));

    private static long ReadLong(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToInt64(ReadValue(properties, names));

    private static double? ReadDouble(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToDouble(ReadValue(properties, names));

    private static bool ReadBoolean(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToBoolean(ReadValue(properties, names));

    private static long ReadFixedPoint(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
    {
        foreach (var name in names)
        {
            if (!properties.TryGetValue(name, out var property)) continue;
            var raw = property.Value is IReadOnlyDictionary<string, GvasProperty> fixedPoint
                ? ReadValue(fixedPoint, "Value")
                : property.Value;
            return GvasValueNavigator.ToInt64(raw) / 1000;
        }
        return 0;
    }

    private static IReadOnlyDictionary<string, int> ReadStatusPoints(
        IReadOnlyDictionary<string, GvasProperty> properties)
    {
        var points = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "GotStatusPointList", "GotExStatusPointList" })
        {
            foreach (var entry in ReadStructArray(properties, field))
            {
                var name = NormalizeStatusPointName(ReadText(entry, "StatusName"));
                if (string.IsNullOrWhiteSpace(name)) continue;
                var value = ReadInt(entry, "StatusPoint");
                points[name] = points.TryGetValue(name, out var current) ? current + value : value;
            }
        }
        return points;
    }

    private static string? NormalizeStatusPointName(string? value)
        => value?.Trim() switch
        {
            "最大HP" or "MaxHP" => "MaxHP",
            "最大SP" or "MaxSP" => "MaxSP",
            "攻撃力" or "攻击力" or "Attack" => "Attack",
            "防御力" or "防御力強化" or "Defense" => "Defense",
            "所持重量" or "负重" or "Weight" => "Weight",
            "作業速度" or "工作速度" or "CraftSpeed" or "WorkSpeed" => "CraftSpeed",
            { Length: > 0 } other => other,
            _ => null
        };

    private static IReadOnlyList<string> ReadTextArray(
        IReadOnlyDictionary<string, GvasProperty> properties,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!properties.TryGetValue(name, out var property)) continue;
            if (property.Value is IReadOnlyList<object?> values)
                return values
                    .Select(GvasValueNavigator.ToText)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();
            if (property.Value is IReadOnlyList<Guid> guids)
                return guids.Select(value => value.ToString("N").ToUpperInvariant()).ToArray();
        }
        return [];
    }

    private int ReadIntDeep(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
        => GvasValueNavigator.ToInt32(_navigator.FindFirst(properties, names));

    private GvasVector? ReadVectorDeep(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
    {
        var value = _navigator.FindFirst(properties, names);
        if (value is GvasVector vector) return vector;
        if (value is IReadOnlyDictionary<string, GvasProperty> nested)
        {
            var x = GvasValueNavigator.ToDouble(_navigator.FindFirst(nested, "X", "x"));
            var y = GvasValueNavigator.ToDouble(_navigator.FindFirst(nested, "Y", "y"));
            var z = GvasValueNavigator.ToDouble(_navigator.FindFirst(nested, "Z", "z")) ?? 0;
            if (x.HasValue && y.HasValue) return new GvasVector(x.Value, y.Value, z);
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
    {
        var text = ReadText(properties, names);
        return DateTimeOffset.TryParse(text, out var value) ? value : null;
    }

    private static string? NormalizeId(object? value)
    {
        if (value is Guid guid)
            return guid == Guid.Empty ? null : guid.ToString("N").ToUpperInvariant();

        var text = GvasValueNavigator.ToText(value);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Guid.TryParse(text, out guid))
            return guid == Guid.Empty ? null : guid.ToString("N").ToUpperInvariant();

        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 32 && !hex.All(character => character == '0')
            ? hex.ToUpperInvariant()
            : null;
    }

    private static string NormalizeEnum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        return (separator >= 0 ? value[(separator + 2)..] : value).Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static DateTimeOffset? ConvertLastOnline(long value)
    {
        if (value <= 0) return null;
        try
        {
            if (value > DateTime.UnixEpoch.Ticks && value <= DateTime.MaxValue.Ticks)
                return new DateTimeOffset(new DateTime(value, DateTimeKind.Utc));
            if (value > 10_000_000_000)
                return DateTimeOffset.FromUnixTimeMilliseconds(value);
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
