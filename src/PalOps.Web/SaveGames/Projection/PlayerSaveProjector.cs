using System.Globalization;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.SaveGames.Projection;

/// <summary>
/// The per-player save does not contain the authoritative nickname, level, inventory
/// contents or Pal records. It contains the player UID, last transform and the IDs of
/// the world-save containers that own those records. These IDs are joined with
/// Level.sav by SaveIndexingService after every document has been parsed.
/// </summary>
public sealed record PlayerSaveProjection(
    IndexedPlayer Player,
    IReadOnlyDictionary<string, string> ItemContainerIds,
    IReadOnlyDictionary<string, string> PalContainerIds,
    IReadOnlyDictionary<string, string> Diagnostics);

public interface IPlayerSaveProjector
{
    PlayerSaveProjection Project(GvasDocument document, string sourceFile);
}

public sealed class PlayerSaveProjector : IPlayerSaveProjector
{
    private static readonly IReadOnlyDictionary<string, string> ItemContainerFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["inventory"] = "CommonContainerId",
            ["drop"] = "DropSlotContainerId",
            ["keyItem"] = "EssentialContainerId",
            ["weapon"] = "WeaponLoadOutContainerId",
            ["equipment"] = "PlayerEquipArmorContainerId",
            ["food"] = "FoodEquipContainerId"
        };

    private static readonly IReadOnlyDictionary<string, string> PalContainerFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["party"] = "OtomoCharacterContainerId",
            ["palbox"] = "PalStorageContainerId"
        };

    public PlayerSaveProjection Project(GvasDocument document, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        var saveData = GetStruct(document.Properties, "SaveData")
                       ?? throw new InvalidDataException($"玩家存档 {sourceFile} 缺少 SaveData。" );
        var sourceStem = Path.GetFileNameWithoutExtension(sourceFile);
        var playerUid = NormalizeGuid(ReadNested(saveData, "PlayerUId"))
                        ?? NormalizeGuid(sourceStem)
                        ?? sourceStem.ToUpperInvariant();

        var position = ReadVector(saveData, "LastTransform", "Translation");
        var lastSeen = ReadDateTime(ReadNested(saveData, "LastOnlineDateTime"));
        var platform = GvasValueNavigator.ToText(ReadNested(saveData, "PlayerPlatform")) ?? string.Empty;

        var itemContainers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inventoryInfo = GetStruct(saveData, "InventoryInfo");
        if (inventoryInfo is not null)
        {
            foreach (var pair in ItemContainerFields)
            {
                var containerId = NormalizeGuid(ReadNested(inventoryInfo, pair.Value, "ID"));
                if (!string.IsNullOrWhiteSpace(containerId)) itemContainers[pair.Key] = containerId;
            }
        }

        var palContainers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in PalContainerFields)
        {
            var containerId = NormalizeGuid(ReadNested(saveData, pair.Value, "ID"));
            if (!string.IsNullOrWhiteSpace(containerId)) palContainers[pair.Key] = containerId;
        }

        // The world-save profile replaces these placeholder profile values during the
        // domain join. Position, last-seen time and source filename remain authoritative
        // from Players/<uid>.sav.
        // PlayerPlatform is only an enum such as EPalPlayerPlatform::Steam; it is
        // not the Steam/EOS account identifier used by REST and RCON. Keep UserId
        // empty so the live-player merge can supply a real external identifier instead
        // of publishing a misleading platform enum as an account ID.
        var player = new IndexedPlayer(
            playerUid,
            string.Empty,
            playerUid,
            0,
            null,
            null,
            string.Empty,
            lastSeen,
            position?.X,
            position?.Y,
            position?.Z,
            SourceFile: sourceFile.Replace(Path.DirectorySeparatorChar, '/'));

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["saveGameClass"] = document.SaveGameClassName,
            ["sourceFile"] = sourceFile,
            ["playerUid"] = playerUid,
            ["itemContainerIds"] = itemContainers.Count.ToString(CultureInfo.InvariantCulture),
            ["palContainerIds"] = palContainers.Count.ToString(CultureInfo.InvariantCulture),
            ["playerPlatform"] = platform
        };

        return new PlayerSaveProjection(player, itemContainers, palContainers, diagnostics);
    }

    private static IReadOnlyDictionary<string, GvasProperty>? GetStruct(
        IReadOnlyDictionary<string, GvasProperty> properties,
        string name)
        => properties.TryGetValue(name, out var property)
           && property.Value is IReadOnlyDictionary<string, GvasProperty> nested
            ? nested
            : null;

    private static object? ReadNested(
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

    private static GvasVector? ReadVector(
        IReadOnlyDictionary<string, GvasProperty> properties,
        params string[] path)
        => ReadNested(properties, path) as GvasVector;

    private static DateTimeOffset? ReadDateTime(object? value)
    {
        try
        {
            return value switch
            {
                ulong ticks when ticks <= long.MaxValue && ticks > 0
                    => new DateTimeOffset(new DateTime((long)ticks, DateTimeKind.Utc)),
                long ticks when ticks > 0
                    => new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)),
                _ => DateTimeOffset.TryParse(
                    GvasValueNavigator.ToText(value),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsed)
                    ? parsed.ToUniversalTime()
                    : null
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? NormalizeGuid(object? value)
    {
        if (value is Guid guid) return guid.ToString("N").ToUpperInvariant();
        var text = GvasValueNavigator.ToText(value);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Guid.TryParse(text, out guid)) return guid.ToString("N").ToUpperInvariant();

        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 32 ? hex.ToUpperInvariant() : null;
    }
}
