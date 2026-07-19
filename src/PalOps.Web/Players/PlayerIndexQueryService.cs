using PalOps.Web.Catalog;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.SaveGames;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.Players;

public sealed record PlayerListQuery(
    string? Query,
    string? Status,
    string? GuildId,
    int? MinLevel,
    int? MaxLevel,
    string? Sort,
    int Page,
    int PageSize);

public sealed record PlayerItemQuery(
    string? Query,
    string? ContainerType,
    int? Quality,
    bool? Recognized,
    int Page,
    int PageSize);

public sealed record PlayerPalQuery(
    string? Query,
    string? Gender,
    string? ContainerType,
    bool? IsBoss,
    bool? IsLucky,
    bool? IsTower,
    string? PassiveId,
    string? SkillId,
    int? MinLevel,
    int? MaxLevel,
    int Page,
    int PageSize);

public interface IPlayerIndexQueryService
{
    Task<PagedData<PlayerListItemV1>> GetPlayersAsync(PlayerListQuery query, CancellationToken cancellationToken = default);
    Task<PlayerDetailV1> GetPlayerAsync(string playerUid, CancellationToken cancellationToken = default);
    Task<PagedData<PlayerItemV1>> GetItemsAsync(string playerUid, PlayerItemQuery query, CancellationToken cancellationToken = default);
    Task<PagedData<PlayerPalV1>> GetPalsAsync(string playerUid, PlayerPalQuery query, CancellationToken cancellationToken = default);
}

public sealed class PlayerIndexQueryService(
    ISaveIndexRepository repository,
    IPlayerAggregationService livePlayers,
    ICatalogService catalog,
    IGameNameLookup names,
    ILogger<PlayerIndexQueryService> logger) : IPlayerIndexQueryService
{
    private sealed record LivePlayerSnapshot(IReadOnlyList<PlayerResponse> Players, bool Available);

    public async Task<PagedData<PlayerListItemV1>> GetPlayersAsync(
        PlayerListQuery query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await repository.GetCurrentAsync(cancellationToken);
        var live = await TryGetLiveAsync(cancellationToken);
        var rows = BuildPlayerRows(snapshot, live.Players);

        IEnumerable<PlayerListItemV1> filtered = rows;
        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var needle = query.Query.Trim();
            filtered = filtered.Where(player =>
                Contains(player.Name, needle)
                || Contains(player.PlayerUid, needle)
                || Contains(player.UserId, needle)
                || Contains(player.SteamId, needle)
                || Contains(player.GuildName, needle));
        }

        if (query.Status?.Equals("online", StringComparison.OrdinalIgnoreCase) == true)
            filtered = filtered.Where(player => player.Online);
        else if (query.Status?.Equals("offline", StringComparison.OrdinalIgnoreCase) == true)
            filtered = filtered.Where(player => !player.Online);

        if (!string.IsNullOrWhiteSpace(query.GuildId))
        {
            var guild = query.GuildId.Trim();
            filtered = filtered.Where(player => Equal(player.GuildId, guild));
        }

        if (query.MinLevel.HasValue) filtered = filtered.Where(player => player.Level >= query.MinLevel.Value);
        if (query.MaxLevel.HasValue) filtered = filtered.Where(player => player.Level <= query.MaxLevel.Value);

        filtered = (query.Sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "name" => filtered.OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase),
            "level" => filtered.OrderByDescending(player => player.Level).ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase),
            "lastseen" => filtered.OrderByDescending(player => player.LastSeenAt).ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase),
            "ping" => filtered.OrderBy(player => player.Ping ?? double.MaxValue).ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(player => player.Online)
                .ThenByDescending(player => player.LastSeenAt)
                .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
        };

        return Page(filtered, query.Page, query.PageSize);
    }

    public async Task<PlayerDetailV1> GetPlayerAsync(
        string playerUid,
        CancellationToken cancellationToken = default)
    {
        var identifier = RequireIdentifier(playerUid);
        var snapshot = await repository.GetCurrentAsync(cancellationToken);
        var live = await TryGetLiveAsync(cancellationToken);
        var indexed = snapshot?.Players.FirstOrDefault(player => Matches(player, identifier));
        var current = live.Players.FirstOrDefault(player => Matches(player, identifier)
            || (indexed is not null && Matches(player, indexed.PlayerUid))
            || (indexed is not null && Matches(player, indexed.UserId)));

        if (indexed is null && current is null)
            throw new KeyNotFoundException("未找到指定玩家。");

        var uid = indexed?.PlayerUid ?? current!.PlayerUid;
        return new PlayerDetailV1(
            uid,
            current?.UserId ?? indexed?.UserId ?? string.Empty,
            SteamIdFrom(current?.UserId ?? indexed?.UserId),
            current?.Name ?? indexed?.Name ?? uid,
            current?.AccountName ?? indexed?.AccountName ?? string.Empty,
            indexed?.GuildId ?? string.Empty,
            current?.GuildName ?? indexed?.GuildName ?? string.Empty,
            current?.Level ?? indexed?.Level ?? 0,
            indexed?.Experience ?? 0,
            indexed?.Hp ?? 0,
            indexed?.MaxHp ?? 0,
            indexed?.ShieldHp ?? 0,
            indexed?.ShieldMaxHp ?? 0,
            indexed?.FullStomach ?? 0,
            indexed?.StatusPoints ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            current?.Online ?? false,
            current?.Ping,
            current?.LocationX ?? indexed?.X,
            current?.LocationY ?? indexed?.Y,
            current?.LocationZ ?? indexed?.Z,
            indexed?.LastSeenAt,
            indexed?.SourceFile ?? string.Empty,
            snapshot?.Items.Count(item => Equal(item.PlayerUid, uid)) ?? 0,
            snapshot?.Pals.Count(pal => Equal(pal.PlayerUid, uid)) ?? 0,
            BuildSource(snapshot, live.Available));
    }

    public async Task<PagedData<PlayerItemV1>> GetItemsAsync(
        string playerUid,
        PlayerItemQuery query,
        CancellationToken cancellationToken = default)
    {
        var identifier = RequireIdentifier(playerUid);
        var snapshot = await repository.GetCurrentAsync(cancellationToken)
            ?? throw new SaveIndexUnavailableException("本地存档索引尚未建立，请先完成一次存档解析。");
        var player = snapshot.Players.FirstOrDefault(candidate => Matches(candidate, identifier))
            ?? throw new KeyNotFoundException("存档索引中不存在该玩家。");
        var lookup = await catalog.GetLookupAsync("item", cancellationToken);

        IEnumerable<PlayerItemV1> items = snapshot.Items
            .Where(item => Equal(item.PlayerUid, player.PlayerUid))
            .Select(item => ToItem(item, lookup));

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var needle = query.Query.Trim();
            items = items.Where(item => Contains(item.ItemId, needle)
                || Contains(item.NameZh, needle)
                || Contains(item.NameEn, needle)
                || Contains(item.Category, needle));
        }
        if (!string.IsNullOrWhiteSpace(query.ContainerType))
            items = items.Where(item => Equal(item.ContainerType, query.ContainerType));
        if (query.Quality.HasValue) items = items.Where(item => item.Quality == query.Quality.Value);
        if (query.Recognized.HasValue) items = items.Where(item => item.Recognized == query.Recognized.Value);

        return Page(items.OrderBy(item => ContainerOrder(item.ContainerType)).ThenBy(item => item.SlotIndex), query.Page, query.PageSize);
    }

    public async Task<PagedData<PlayerPalV1>> GetPalsAsync(
        string playerUid,
        PlayerPalQuery query,
        CancellationToken cancellationToken = default)
    {
        var identifier = RequireIdentifier(playerUid);
        var snapshot = await repository.GetCurrentAsync(cancellationToken)
            ?? throw new SaveIndexUnavailableException("本地存档索引尚未建立，请先完成一次存档解析。");
        var player = snapshot.Players.FirstOrDefault(candidate => Matches(candidate, identifier))
            ?? throw new KeyNotFoundException("存档索引中不存在该玩家。");
        var lookup = await catalog.GetLookupAsync("pal", cancellationToken);

        IEnumerable<PlayerPalV1> pals = snapshot.Pals
            .Where(pal => Equal(pal.PlayerUid, player.PlayerUid))
            .Select(pal => ToPal(pal, lookup));

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var needle = query.Query.Trim();
            pals = pals.Where(pal => Contains(pal.PalId, needle)
                || Contains(pal.NameZh, needle)
                || Contains(pal.NameEn, needle)
                || Contains(pal.Nickname, needle)
                || pal.Passives.Any(value => Contains(value.Id, needle) || Contains(value.Name, needle))
                || pal.Skills.Any(value => Contains(value.Id, needle) || Contains(value.Name, needle)));
        }
        if (!string.IsNullOrWhiteSpace(query.Gender)) pals = pals.Where(pal => Equal(pal.Gender, query.Gender));
        if (!string.IsNullOrWhiteSpace(query.ContainerType)) pals = pals.Where(pal => Equal(pal.ContainerType, query.ContainerType));
        if (query.IsBoss.HasValue) pals = pals.Where(pal => pal.IsBoss == query.IsBoss.Value);
        if (query.IsLucky.HasValue) pals = pals.Where(pal => pal.IsLucky == query.IsLucky.Value);
        if (query.IsTower.HasValue) pals = pals.Where(pal => pal.IsTower == query.IsTower.Value);
        if (!string.IsNullOrWhiteSpace(query.PassiveId)) pals = pals.Where(pal => pal.Passives.Any(value => Equal(value.Id, query.PassiveId)));
        if (!string.IsNullOrWhiteSpace(query.SkillId)) pals = pals.Where(pal => pal.Skills.Any(value => Equal(value.Id, query.SkillId)));
        if (query.MinLevel.HasValue) pals = pals.Where(pal => pal.Level >= query.MinLevel.Value);
        if (query.MaxLevel.HasValue) pals = pals.Where(pal => pal.Level <= query.MaxLevel.Value);

        return Page(pals.OrderByDescending(pal => pal.Level).ThenBy(pal => pal.NameZh, StringComparer.OrdinalIgnoreCase), query.Page, query.PageSize);
    }

    private IReadOnlyList<PlayerListItemV1> BuildPlayerRows(
        SaveIndexSnapshot? snapshot,
        IReadOnlyList<PlayerResponse> live)
    {
        var rows = new List<PlayerListItemV1>();
        if (snapshot is not null)
        {
            foreach (var player in snapshot.Players)
            {
                var current = live.FirstOrDefault(candidate => Matches(candidate, player.PlayerUid)
                    || Matches(candidate, player.UserId));
                rows.Add(ToListItem(player, current, snapshot));
            }
        }

        foreach (var current in live)
        {
            if (rows.Any(row => Matches(current, row.PlayerUid) || Matches(current, row.UserId))) continue;
            rows.Add(new PlayerListItemV1(
                current.PlayerUid,
                current.UserId,
                SteamIdFrom(current.UserId),
                current.Name,
                current.Level ?? 0,
                current.Online,
                current.GuildName,
                string.Empty,
                current.Ping,
                null,
                null,
                current.LocationX,
                current.LocationY,
                current.LocationZ,
                "officialRest",
                "officialRest",
                "officialRest"));
        }
        return rows;
    }

    private static PlayerListItemV1 ToListItem(IndexedPlayer player, PlayerResponse? live, SaveIndexSnapshot snapshot)
        => new(
            player.PlayerUid,
            live?.UserId ?? player.UserId,
            SteamIdFrom(live?.UserId ?? player.UserId),
            live?.Name ?? player.Name,
            live?.Level ?? player.Level,
            live?.Online ?? false,
            live?.GuildName ?? player.GuildName ?? string.Empty,
            player.GuildId ?? string.Empty,
            live?.Ping,
            player.LastSeenAt,
            snapshot.ParsedAt,
            live?.LocationX ?? player.X,
            live?.LocationY ?? player.Y,
            live?.LocationZ ?? player.Z,
            "save",
            live is null ? "unavailable" : "officialRest",
            live is null ? "save" : "officialRest");

    private static PlayerItemV1 ToItem(IndexedItem item, IReadOnlyDictionary<string, CatalogEntry> lookup)
    {
        lookup.TryGetValue(item.ItemId, out var entry);
        return new PlayerItemV1(
            item.PlayerUid,
            item.ContainerType,
            ContainerLabel(item.ContainerType),
            item.SlotIndex,
            item.ItemId,
            entry?.NameZh ?? item.ItemId,
            entry?.NameEn ?? string.Empty,
            entry?.Category ?? "未识别",
            entry?.ImageUrl ?? "/catalog/items/_placeholder.svg",
            item.Quantity,
            item.Quality,
            item.Durability,
            item.DynamicItemId,
            entry is not null);
    }

    private PlayerPalV1 ToPal(IndexedPal pal, IReadOnlyDictionary<string, CatalogEntry> lookup)
    {
        lookup.TryGetValue(pal.PalId, out var entry);
        return new PlayerPalV1(
            pal.InstanceId,
            pal.PlayerUid,
            pal.ContainerType,
            PalContainerLabel(pal.ContainerType),
            pal.SlotIndex,
            pal.PalId,
            entry?.NameZh ?? pal.PalId,
            entry?.NameEn ?? string.Empty,
            entry?.Category ?? "未识别",
            entry?.ImageUrl ?? "/catalog/pals/_placeholder.svg",
            pal.Nickname,
            pal.Level,
            pal.Experience,
            pal.Gender,
            pal.IsLucky,
            pal.IsBoss,
            pal.IsTower,
            pal.Hp,
            pal.MaxHp,
            pal.Melee,
            pal.Ranged,
            pal.Defense,
            pal.WorkSpeed,
            pal.Rank,
            pal.Passives.Select(id => new NamedGameValueV1(id, names.Resolve("passives", id))).ToArray(),
            pal.Skills.Select(id => new NamedGameValueV1(id, names.Resolve("skills", id))).ToArray(),
            entry is not null);
    }

    private async Task<LivePlayerSnapshot> TryGetLiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new LivePlayerSnapshot(await livePlayers.GetOnlinePlayersAsync(cancellationToken), true);
        }
        catch (Exception ex) when (ex is ExternalApiException or HttpRequestException)
        {
            logger.LogWarning(ex, "Live player query failed; local save index remains available.");
            return new LivePlayerSnapshot([], false);
        }
    }

    private static PlayerSourceV1 BuildSource(SaveIndexSnapshot? snapshot, bool liveAvailable)
    {
        if (snapshot is null)
            return new PlayerSourceV1("live-only", false, liveAvailable, null, null, false, "本地存档索引尚未建立。");
        return new PlayerSourceV1(
            liveAvailable ? "save+live" : "save-only",
            true,
            liveAvailable,
            snapshot.SnapshotId,
            snapshot.ParsedAt,
            false,
            liveAvailable ? "存档数据与实时在线状态已合并。" : "实时接口不可用，当前显示存档快照。");
    }

    private static PagedData<T> Page<T>(IEnumerable<T> source, int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var array = source as T[] ?? source.ToArray();
        return new PagedData<T>(
            array.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToArray(),
            array.Length,
            safePage,
            safePageSize);
    }

    private static string RequireIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("玩家 UID 不能为空。", nameof(value));
        return value.Trim();
    }

    private static bool Matches(IndexedPlayer player, string identifier)
        => Equal(player.PlayerUid, identifier) || Equal(player.UserId, identifier) || Equal(SteamIdFrom(player.UserId), identifier);

    private static bool Matches(PlayerResponse player, string identifier)
        => Equal(player.PlayerUid, identifier) || Equal(player.UserId, identifier) || Equal(SteamIdFrom(player.UserId), identifier);

    private static bool Contains(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool Equal(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string SteamIdFrom(string? userId)
        => !string.IsNullOrWhiteSpace(userId) && userId.StartsWith("steam_", StringComparison.OrdinalIgnoreCase)
            ? userId[6..]
            : string.Empty;

    private static int ContainerOrder(string container)
        => container switch
        {
            "inventory" => 0,
            "quickSlot" => 1,
            "weapon" => 2,
            "equipment" => 3,
            "food" => 4,
            "keyItem" => 5,
            "drop" => 6,
            _ => 20
        };

    private static string ContainerLabel(string container)
        => container switch
        {
            "inventory" => "普通背包",
            "quickSlot" => "快捷栏",
            "weapon" => "武器栏",
            "equipment" => "装备栏",
            "food" => "食物栏",
            "keyItem" => "关键物品",
            "drop" => "掉落栏",
            _ => "未识别容器"
        };

    private static string PalContainerLabel(string container)
        => container switch
        {
            "party" => "队伍",
            "palbox" => "帕鲁终端",
            "base" => "据点工作",
            _ => "未识别容器"
        };
}
