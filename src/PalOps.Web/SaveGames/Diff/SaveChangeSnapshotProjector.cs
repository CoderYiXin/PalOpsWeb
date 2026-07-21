using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.SaveGames.Diff;

public interface ISaveChangeSnapshotProjector
{
    SaveChangeSnapshot Project(SaveIndexSnapshot source);
}

public sealed class SaveChangeSnapshotProjector : ISaveChangeSnapshotProjector
{
    private static readonly HashSet<string> ImportantContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "keyItem", "weapon", "equipment"
    };

    public SaveChangeSnapshot Project(SaveIndexSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var players = source.Players
            .GroupBy(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(player => player.LastSeenAt).First())
            .Select(player => new SaveChangePlayer(
                Normalize(player.PlayerUid),
                Normalize(player.UserId),
                player.Name?.Trim() ?? string.Empty,
                player.Level,
                player.Experience,
                Normalize(player.GuildId),
                player.X,
                player.Y,
                player.Z))
            .OrderBy(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var guilds = source.Guilds
            .GroupBy(guild => guild.GuildId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(guild => new SaveChangeGuild(
                Normalize(guild.GuildId),
                guild.Name?.Trim() ?? string.Empty,
                guild.Level,
                Normalize(guild.LeaderPlayerUid),
                guild.MemberPlayerUids
                    .Where(uid => !string.IsNullOrWhiteSpace(uid))
                    .Select(Normalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(uid => uid, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(guild => guild.GuildId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var bases = source.Bases
            .GroupBy(baseCamp => baseCamp.BaseId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(baseCamp => new SaveChangeBase(
                Normalize(baseCamp.BaseId),
                Normalize(baseCamp.GuildId),
                baseCamp.X,
                baseCamp.Y,
                baseCamp.Z,
                baseCamp.WorkerCount,
                baseCamp.MapObjectCount,
                baseCamp.AssociationType?.Trim() ?? string.Empty))
            .OrderBy(baseCamp => baseCamp.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = source.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.PlayerUid) && !string.IsNullOrWhiteSpace(item.ItemId))
            .GroupBy(item => new ItemKey(
                Normalize(item.PlayerUid),
                item.ContainerType?.Trim() ?? string.Empty,
                item.ItemId.Trim(),
                item.Quality,
                Normalize(item.DynamicItemId)), ItemKeyComparer.Instance)
            .Select(group => new SaveChangeItem(
                group.Key.PlayerUid,
                group.Key.ContainerType,
                group.Key.ItemId,
                group.Key.Quality,
                group.Key.DynamicItemId,
                checked(group.Sum(item => item.Quantity)),
                IsImportant(group.Key)))
            .Where(item => item.Quantity != 0)
            .OrderBy(item => item.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ContainerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Quality)
            .ThenBy(item => item.DynamicItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pals = source.Pals
            .Where(pal => !string.IsNullOrWhiteSpace(pal.PlayerUid) && !string.IsNullOrWhiteSpace(pal.PalId))
            .GroupBy(pal => new PalKey(Normalize(pal.PlayerUid), pal.PalId.Trim()), PalKeyComparer.Instance)
            .Select(group => new SaveChangePal(
                group.Key.PlayerUid,
                group.Key.PalId,
                group.Count(),
                group.Count(pal => pal.IsLucky),
                group.Count(pal => pal.IsBoss),
                group.Average(pal => pal.Level)))
            .OrderBy(pal => pal.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pal => pal.PalId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SaveChangeSnapshot(
            1,
            source.SnapshotId,
            source.WorldId,
            source.CreatedAt,
            source.ParsedAt,
            source.LevelSha256,
            players,
            guilds,
            bases,
            items,
            pals);
    }

    private static bool IsImportant(ItemKey key)
        => ImportantContainers.Contains(key.ContainerType)
           || key.Quality > 0
           || !string.IsNullOrWhiteSpace(key.DynamicItemId);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private readonly record struct ItemKey(
        string PlayerUid,
        string ContainerType,
        string ItemId,
        int Quality,
        string DynamicItemId);

    private sealed class ItemKeyComparer : IEqualityComparer<ItemKey>
    {
        public static ItemKeyComparer Instance { get; } = new();
        public bool Equals(ItemKey x, ItemKey y)
            => x.Quality == y.Quality
               && StringComparer.OrdinalIgnoreCase.Equals(x.PlayerUid, y.PlayerUid)
               && StringComparer.OrdinalIgnoreCase.Equals(x.ContainerType, y.ContainerType)
               && StringComparer.OrdinalIgnoreCase.Equals(x.ItemId, y.ItemId)
               && StringComparer.OrdinalIgnoreCase.Equals(x.DynamicItemId, y.DynamicItemId);
        public int GetHashCode(ItemKey value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PlayerUid),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ContainerType),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ItemId),
                value.Quality,
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.DynamicItemId));
    }

    private readonly record struct PalKey(string PlayerUid, string PalId);

    private sealed class PalKeyComparer : IEqualityComparer<PalKey>
    {
        public static PalKeyComparer Instance { get; } = new();
        public bool Equals(PalKey x, PalKey y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.PlayerUid, y.PlayerUid)
               && StringComparer.OrdinalIgnoreCase.Equals(x.PalId, y.PalId);
        public int GetHashCode(PalKey value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PlayerUid),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PalId));
    }
}
