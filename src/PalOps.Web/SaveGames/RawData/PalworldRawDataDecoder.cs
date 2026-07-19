using PalOps.Web.SaveGames.Binary;

namespace PalOps.Web.SaveGames.RawData;

public sealed record DecodedCharacterRawData(
    IReadOnlyDictionary<string, GvasProperty> Properties,
    Guid GroupId,
    byte[] UnknownBytes,
    int TrailingBytes);

public sealed record DecodedCharacterContainerRawData(
    Guid PlayerUid,
    Guid InstanceId,
    byte PermissionTribeId,
    int TrailingBytes);

public sealed record DecodedItemSlotRawData(
    int SlotIndex,
    int Quantity,
    string ItemId,
    Guid CreatedWorldId,
    Guid LocalDynamicId,
    int TrailingBytes);

public sealed record DecodedGuildMember(Guid PlayerUid, long LastOnlineRaw, string PlayerName);

public sealed record DecodedBaseCampRawData(
    Guid BaseId,
    string Name,
    double X,
    double Y,
    double Z,
    int TrailingBytes);

public sealed record DecodedGroupRawData(
    string GroupType,
    Guid GroupId,
    string GroupName,
    string GuildName,
    Guid? AdminPlayerUid,
    int BaseCampLevel,
    IReadOnlyList<Guid> BaseIds,
    IReadOnlyList<DecodedGuildMember> Members,
    int TrailingBytes);

public interface IPalworldRawDataDecoder
{
    bool TryDecodeCharacter(object? value, out DecodedCharacterRawData decoded);
    bool TryDecodeCharacterContainer(object? value, out DecodedCharacterContainerRawData decoded);
    bool TryDecodeItemSlot(object? value, out DecodedItemSlotRawData decoded);
    bool TryDecodeGroup(object? value, string groupType, out DecodedGroupRawData decoded);
    bool TryDecodeBaseCamp(object? value, Guid expectedBaseId, out DecodedBaseCampRawData decoded);
}

/// <summary>
/// Read-only decoders for Palworld-specific byte arrays. Newer game versions append
/// versioned fields to several records, so the stable prefix is decoded and unknown
/// tails are reported instead of requiring an exact legacy byte length.
/// </summary>
public sealed class PalworldRawDataDecoder(IGvasParser parser) : IPalworldRawDataDecoder
{
    private const int MaximumRawArrayElements = 2_000_000;

    // Palworld 1.0 guild records place this version marker before the admin UID
    // and member array. The layout matches palsav/rawdata/group.py.
    private static readonly byte[] GuildV1Marker =
        [0x02, 0x00, 0x00, 0x00, 0x02, 0x03, 0x00, 0x00, 0x00, 0x00];

    public bool TryDecodeCharacter(object? value, out DecodedCharacterRawData decoded)
    {
        decoded = default!;
        if (!TryGetBytes(value, out var bytes) || bytes.Length < 20) return false;
        try
        {
            var bag = parser.ParsePropertyBag(bytes);
            if (bag.Remaining.Length < 20) return false;
            var unknown = bag.Remaining[..4].ToArray();
            var groupId = new Guid(bag.Remaining.Slice(4, 16).Span);
            decoded = new DecodedCharacterRawData(
                bag.Properties,
                groupId,
                unknown,
                Math.Max(0, bag.Remaining.Length - 20));
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public bool TryDecodeCharacterContainer(object? value, out DecodedCharacterContainerRawData decoded)
    {
        decoded = default!;
        if (!TryGetBytes(value, out var bytes) || bytes.Length < 32) return false;
        try
        {
            var reader = new GvasArchiveReader(bytes);
            var playerUid = reader.ReadGuid();
            var instanceId = reader.ReadGuid();
            var permission = reader.Remaining > 0 ? reader.ReadByte() : (byte)0;
            decoded = new DecodedCharacterContainerRawData(
                playerUid,
                instanceId,
                permission,
                reader.Remaining);
            // An all-zero instance GUID is a valid empty container slot, not a
            // decoder failure. The projector counts it as structurally decoded
            // and simply omits it from the occupied instance reverse index.
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            decoded = default!;
            return false;
        }
    }

    public bool TryDecodeItemSlot(object? value, out DecodedItemSlotRawData decoded)
    {
        decoded = default!;
        if (!TryGetBytes(value, out var bytes) || bytes.Length < 8 + 1 + 32) return false;
        try
        {
            var reader = new GvasArchiveReader(bytes);
            var slotIndex = reader.ReadInt32();
            var quantity = reader.ReadInt32();
            var itemId = reader.ReadFString();
            if (reader.Remaining < 32) return false;
            var createdWorldId = reader.ReadGuid();
            var localDynamicId = reader.ReadGuid();
            decoded = new DecodedItemSlotRawData(
                slotIndex,
                quantity,
                itemId,
                createdWorldId,
                localDynamicId,
                reader.Remaining);
            return slotIndex >= 0 && quantity >= 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException or ArgumentException)
        {
            decoded = default!;
            return false;
        }
    }

    public bool TryDecodeGroup(object? value, string groupType, out DecodedGroupRawData decoded)
    {
        decoded = default!;
        if (!TryGetBytes(value, out var bytes) || bytes.Length < 24 || string.IsNullOrWhiteSpace(groupType)) return false;
        try
        {
            var reader = new GvasArchiveReader(bytes);
            var groupId = reader.ReadGuid();
            var groupName = reader.ReadFString();
            SkipInstanceHandles(reader);

            var baseIds = new List<Guid>();
            var guildName = groupName;
            Guid? admin = null;
            var level = 0;
            var members = new List<DecodedGuildMember>();
            var trailingBytes = 0;

            if (IsGroupType(groupType, "Organization"))
            {
                _ = reader.ReadByte();
                // Palworld 1.0 organizations append twelve stable bytes followed by
                // optional version data that is not needed by the read-only index.
                if (reader.Remaining >= 12) reader.Skip(12);
                trailingBytes = reader.Remaining;
            }
            else if (IsGroupType(groupType, "IndependentGuild"))
            {
                _ = reader.ReadByte();
                level = reader.ReadInt32();
                _ = ReadGuidArray(reader);
                guildName = reader.ReadFString();
                var playerUid = reader.ReadGuid();
                var secondName = reader.ReadFString();
                var lastOnline = reader.ReadInt64();
                var playerName = reader.ReadFString();
                if (!string.IsNullOrWhiteSpace(secondName)) guildName = secondName;
                members.Add(new DecodedGuildMember(playerUid, lastOnline, playerName));
                admin = playerUid;
                trailingBytes = reader.Remaining;
            }
            else if (IsGroupType(groupType, "Guild"))
            {
                _ = reader.ReadByte(); // organization type
                reader.Skip(4);        // Palworld 1.0 leading guild bytes
                baseIds.AddRange(ReadGuidArray(reader));
                _ = reader.ReadInt32(); // unknown_1
                level = reader.ReadInt32();
                _ = ReadGuidArray(reader); // base-camp point map-object instance IDs
                guildName = reader.ReadFString();
                _ = reader.ReadGuid(); // last guild-name modifier player UID
                reader.Skip(4);        // unknown_2

                var tail = reader.ReadMemory(reader.Remaining);
                if (TryReadGuildV1Members(tail, out var v1Admin, out var v1Members, out var v1Trailing))
                {
                    admin = v1Admin;
                    members.AddRange(v1Members);
                    trailingBytes = v1Trailing;
                }
                else
                {
                    // The stable guild prefix is still useful when a future game
                    // version changes the member suffix. Membership is reconciled
                    // from CharacterSaveParameterMap by WorldSaveProjector.
                    trailingBytes = tail.Length;
                }
            }
            else
            {
                trailingBytes = reader.Remaining;
            }

            decoded = new DecodedGroupRawData(
                groupType,
                groupId,
                groupName,
                string.IsNullOrWhiteSpace(guildName) ? groupName : guildName,
                admin,
                level,
                baseIds,
                members,
                trailingBytes);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException or ArgumentException)
        {
            decoded = default!;
            return false;
        }
    }


    public bool TryDecodeBaseCamp(object? value, Guid expectedBaseId, out DecodedBaseCampRawData decoded)
    {
        decoded = default!;
        if (!TryGetBytes(value, out var bytes) || bytes.Length < 16 + 4 + 1 + (10 * 8)) return false;
        try
        {
            var reader = new GvasArchiveReader(bytes);
            var baseId = reader.ReadGuid();
            var name = reader.ReadFString();
            _ = reader.ReadByte(); // stable BaseCamp state byte
            _ = reader.ReadDouble(); // quaternion X
            _ = reader.ReadDouble(); // quaternion Y
            _ = reader.ReadDouble(); // quaternion Z
            _ = reader.ReadDouble(); // quaternion W
            var x = reader.ReadDouble();
            var y = reader.ReadDouble();
            var z = reader.ReadDouble();
            _ = reader.ReadDouble(); // scale X
            _ = reader.ReadDouble(); // scale Y
            _ = reader.ReadDouble(); // scale Z

            if (expectedBaseId != Guid.Empty && baseId != Guid.Empty && baseId != expectedBaseId) return false;
            if (!IsFiniteWorldCoordinate(x) || !IsFiniteWorldCoordinate(y) || !IsFiniteWorldCoordinate(z)) return false;
            decoded = new DecodedBaseCampRawData(baseId, name.Trim(), x, y, z, reader.Remaining);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException or ArgumentException)
        {
            decoded = default!;
            return false;
        }
    }

    private static bool TryReadGuildV1Members(
        ReadOnlyMemory<byte> tail,
        out Guid adminPlayerUid,
        out IReadOnlyList<DecodedGuildMember> members,
        out int trailingBytes)
    {
        adminPlayerUid = Guid.Empty;
        members = [];
        trailingBytes = tail.Length;

        var markerIndex = tail.Span.IndexOf(GuildV1Marker);
        if (markerIndex < 0) return false;

        try
        {
            var reader = new GvasArchiveReader(tail.Slice(markerIndex + GuildV1Marker.Length));
            adminPlayerUid = reader.ReadGuid();
            var count = ReadCount(reader, "公会成员");
            var decodedMembers = new List<DecodedGuildMember>(Math.Min(count, 4096));
            for (var index = 0; index < count; index++)
            {
                var playerUid = reader.ReadGuid();
                var lastOnline = reader.ReadInt64();
                var playerName = reader.ReadFString();
                if (reader.Remaining > 0) _ = reader.ReadByte(); // Palworld 1.0 member flag
                decodedMembers.Add(new DecodedGuildMember(playerUid, lastOnline, playerName));
            }

            members = decodedMembers;
            trailingBytes = markerIndex + reader.Remaining;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException or ArgumentException)
        {
            adminPlayerUid = Guid.Empty;
            members = [];
            trailingBytes = tail.Length;
            return false;
        }
    }


    private static bool IsFiniteWorldCoordinate(double value)
        => double.IsFinite(value) && Math.Abs(value) <= 10_000_000d;

    public static bool TryGetBytes(object? value, out ReadOnlyMemory<byte> bytes)
    {
        switch (value)
        {
            case byte[] array:
                bytes = array;
                return array.Length > 0;
            case ReadOnlyMemory<byte> memory:
                bytes = memory;
                return memory.Length > 0;
            case IReadOnlyList<object?> values when values.Count is > 0 and <= MaximumRawArrayElements:
                var buffer = new byte[values.Count];
                for (var index = 0; index < values.Count; index++)
                {
                    if (!TryByte(values[index], out buffer[index]))
                    {
                        bytes = default;
                        return false;
                    }
                }
                bytes = buffer;
                return true;
            case IReadOnlyList<byte> byteValues when byteValues.Count is > 0 and <= MaximumRawArrayElements:
                bytes = byteValues.ToArray();
                return true;
            default:
                bytes = default;
                return false;
        }
    }

    private static bool TryByte(object? value, out byte result)
    {
        switch (value)
        {
            case byte number: result = number; return true;
            case sbyte number: result = unchecked((byte)number); return true;
            case short number when number is >= byte.MinValue and <= byte.MaxValue: result = (byte)number; return true;
            case ushort number when number <= byte.MaxValue: result = (byte)number; return true;
            case int number when number is >= byte.MinValue and <= byte.MaxValue: result = (byte)number; return true;
            case uint number when number <= byte.MaxValue: result = (byte)number; return true;
            default: result = 0; return false;
        }
    }

    private static void SkipInstanceHandles(GvasArchiveReader reader)
    {
        var count = ReadCount(reader, "角色句柄");
        var bytes = checked(count * 32);
        reader.Skip(bytes);
    }

    private static IReadOnlyList<Guid> ReadGuidArray(GvasArchiveReader reader)
    {
        var count = ReadCount(reader, "GUID 数组");
        var values = new Guid[count];
        for (var index = 0; index < count; index++) values[index] = reader.ReadGuid();
        return values;
    }

    private static int ReadCount(GvasArchiveReader reader, string label)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumRawArrayElements) throw new InvalidDataException($"{label}数量超过限制。" );
        return checked((int)count);
    }

    private static bool IsGroupType(string value, string suffix)
        => value.EndsWith("::" + suffix, StringComparison.OrdinalIgnoreCase)
           || value.Equals(suffix, StringComparison.OrdinalIgnoreCase);
}
