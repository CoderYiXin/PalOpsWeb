using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Platform.Caching;
using PalOps.Web.Platform.Readiness;

namespace PalOps.Web.Players;

public interface IPlayerAggregationService
{
    Task<IReadOnlyList<PlayerResponse>> GetOnlinePlayersAsync(CancellationToken cancellationToken = default);
}

public interface IPlayerAggregationCache
{
    bool TryGetDefender(TimeSpan maximumAge, out IReadOnlyList<PalDefenderPlayer> players);
    void UpdateDefender(IReadOnlyList<PalDefenderPlayer> players);
}

public sealed class PlayerAggregationCache(IPlatformCache cache) : IPlayerAggregationCache
{
    private const string CacheKey = "players:paldefender";
    private sealed record CacheEntry(IReadOnlyList<PalDefenderPlayer> Players, DateTimeOffset UpdatedAt);

    public bool TryGetDefender(TimeSpan maximumAge, out IReadOnlyList<PalDefenderPlayer> players)
    {
        if (cache.TryGet<CacheEntry>(CacheKey, out var entry) && entry is not null)
        {
            if (entry.Players.Count > 0 && DateTimeOffset.UtcNow - entry.UpdatedAt <= maximumAge)
            {
                players = entry.Players;
                return true;
            }
            cache.Remove(CacheKey);
        }
        players = [];
        return false;
    }

    public void UpdateDefender(IReadOnlyList<PalDefenderPlayer> players) =>
        cache.Set(CacheKey, new CacheEntry(players.ToArray(), DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5), ["players"]);
}

public sealed class PlayerAggregationService(
    IPalworldApiClient palworldApi,
    IPalDefenderApiClient palDefenderApi,
    IPlayerAggregationCache cache,
    IOperationalReadinessGate readinessGate) : IPlayerAggregationService
{
    // PalDefender only enriches the official player snapshot. It must not delay map marker first paint.
    private static readonly TimeSpan DefenderMergeBudget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefenderCacheMaximumAge = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<PlayerResponse>> GetOnlinePlayersAsync(CancellationToken cancellationToken = default)
    {
        var readiness = await readinessGate.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var officialConfigured = readiness.HasAny(OperationalCapability.PalworldRest);
        var defenderConfigured = readiness.HasAny(OperationalCapability.PalDefender);

        if (!officialConfigured && !defenderConfigured)
        {
            throw new ExternalApiException(
                "PLAYER_SOURCES_NOT_CONFIGURED",
                "尚未配置 Palworld REST 或 PalDefender，玩家自动查询保持暂停。");
        }

        var officialTask = officialConfigured
            ? TryGetOfficialAsync(cancellationToken)
            : Task.FromResult(new SourceResult<PalworldPlayer>(false, [], "Palworld REST API 未配置。"));
        var defenderTask = defenderConfigured
            ? TryGetDefenderAsync(cancellationToken)
            : Task.FromResult(new SourceResult<PalDefenderPlayer>(false, [], "PalDefender REST API 未配置。"));
        var official = await officialTask.ConfigureAwait(false);

        if (official.Success)
        {
            var defender = await GetDefenderForMergeAsync(defenderTask).ConfigureAwait(false);
            return MergeOfficialPlayers(official.Players, defender.Players);
        }

        if (defenderConfigured && cache.TryGetDefender(DefenderCacheMaximumAge, out var cachedDefender))
            return CreateDefenderFallback(cachedDefender, "paldefender-cache");

        var defenderFallback = await defenderTask.ConfigureAwait(false);
        if (defenderFallback.Success)
            return CreateDefenderFallback(defenderFallback.Players, "paldefender-fallback");

        throw new ExternalApiException(
            "PLAYER_SOURCES_UNAVAILABLE",
            "已配置的玩家数据接口当前不可用。",
            details: new { officialError = official.Error, defenderError = defenderFallback.Error });
    }

    private async Task<SourceResult<PalDefenderPlayer>> GetDefenderForMergeAsync(Task<SourceResult<PalDefenderPlayer>> defenderTask)
    {
        var completed = await Task.WhenAny(defenderTask, Task.Delay(DefenderMergeBudget, CancellationToken.None)).ConfigureAwait(false);
        if (completed == defenderTask)
            return await defenderTask.ConfigureAwait(false);

        return cache.TryGetDefender(DefenderCacheMaximumAge, out var cached)
            ? new SourceResult<PalDefenderPlayer>(true, cached, "PalDefender 响应较慢，使用最近缓存补充玩家详情。")
            : new SourceResult<PalDefenderPlayer>(false, [], "PalDefender 响应超过首屏合并预算，玩家标记先使用官方 REST 数据。");
    }

    private static IReadOnlyList<PlayerResponse> CreateDefenderFallback(IReadOnlyList<PalDefenderPlayer> players, string source) =>
        players
            .Where(static player => IsOnlineStatus(player.Status))
            .Select(player => new PlayerResponse(
                player.Name,
                player.UserId,
                player.PlayerUid,
                string.Empty,
                player.GuildName,
                null,
                null,
                player.WorldX,
                player.WorldY,
                player.WorldZ,
                true,
                source))
            .Where(static player => !string.IsNullOrWhiteSpace(player.UserId) || !string.IsNullOrWhiteSpace(player.PlayerUid))
            .OrderBy(static player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<PlayerResponse> MergeOfficialPlayers(IReadOnlyList<PalworldPlayer> official, IReadOnlyList<PalDefenderPlayer> defender)
    {
        var byUserId = defender.Where(static player => !string.IsNullOrWhiteSpace(player.UserId))
            .GroupBy(static player => player.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byUid = defender.Where(static player => !string.IsNullOrWhiteSpace(player.PlayerUid))
            .GroupBy(static player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        return official
            .GroupBy(static player => !string.IsNullOrWhiteSpace(player.UserId) ? player.UserId : player.PlayerId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Select(player =>
            {
                PalDefenderPlayer? detail = null;
                if (!string.IsNullOrWhiteSpace(player.UserId)) byUserId.TryGetValue(player.UserId, out detail);
                if (detail is null && !string.IsNullOrWhiteSpace(player.PlayerId)) byUid.TryGetValue(player.PlayerId, out detail);
                return new PlayerResponse(
                    player.Name,
                    player.UserId,
                    player.PlayerId,
                    player.AccountName,
                    detail?.GuildName ?? string.Empty,
                    player.Level,
                    player.Ping,
                    player.LocationX,
                    player.LocationY,
                    detail?.WorldZ,
                    true,
                    detail is null ? "palworld" : "palworld+paldefender");
            })
            .OrderBy(static player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SourceResult<PalworldPlayer>> TryGetOfficialAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new(true, await palworldApi.GetOnlinePlayersAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, [], "Palworld REST 请求超时。");
        }
        catch (Exception exception) when (exception is ExternalApiException or HttpRequestException)
        {
            return new(false, [], exception.Message);
        }
    }

    private async Task<SourceResult<PalDefenderPlayer>> TryGetDefenderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var players = await palDefenderApi.GetKnownPlayersAsync(cancellationToken).ConfigureAwait(false);
            cache.UpdateDefender(players);
            return new(true, players, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, [], "PalDefender REST 请求超时。");
        }
        catch (Exception exception) when (exception is ExternalApiException or HttpRequestException)
        {
            return new(false, [], exception.Message);
        }
    }

    private static bool IsOnlineStatus(string status) =>
        status.Equals("online", StringComparison.OrdinalIgnoreCase)
        || status.Contains("connected", StringComparison.OrdinalIgnoreCase)
        || status.Contains("online", StringComparison.OrdinalIgnoreCase);

    private sealed record SourceResult<T>(bool Success, IReadOnlyList<T> Players, string? Error);
}
