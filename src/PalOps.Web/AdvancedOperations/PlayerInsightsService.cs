using PalOps.Web.PlayerDiscipline;
using PalOps.Web.Players;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.AdvancedOperations;

public interface IPlayerInsightsService
{
    Task<PlayerInsightsDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<PlayerInsightNote> UpdateNoteAsync(string playerKey, PlayerInsightNoteWriteRequest request, string actor, CancellationToken cancellationToken = default);
}

public sealed class PlayerInsightsService(
    IAdvancedOperationsRepository repository,
    ISaveIndexRepository saveIndex,
    IServiceScopeFactory scopeFactory,
    IPlayerDisciplineService discipline,
    AdvancedOperationsValidator validator) : IPlayerInsightsService
{
    public async Task<PlayerInsightsDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var stateTask = repository.ReadAsync(cancellationToken);
        var snapshotTask = saveIndex.GetCurrentAsync(cancellationToken);
        var warnings = new List<string>();

        IReadOnlyList<PalOps.Web.Contracts.PlayerResponse> online = [];
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var playerAggregation = scope.ServiceProvider.GetRequiredService<IPlayerAggregationService>();
            online = await playerAggregation.GetOnlinePlayersAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { warnings.Add("在线玩家接口不可用：" + Limit(ex.Message)); }

        PlayerDisciplineDashboard? disciplineDashboard = null;
        try { disciplineDashboard = await discipline.GetDashboardAsync(cancellationToken); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { warnings.Add("纪律数据不可用：" + Limit(ex.Message)); }

        var state = await stateTask;
        var snapshot = await snapshotTask;
        if (snapshot is null)
        {
            warnings.Add("尚无可用存档索引，玩家洞察仅显示在线数据。");
            var onlineOnly = online.Select(player => BuildOnlineOnly(player, state)).ToArray();
            return new(onlineOnly.Length, onlineOnly.Length, onlineOnly.Count(static item => item.RiskScore >= 70), 0, null, onlineOnly, warnings, DateTimeOffset.UtcNow);
        }

        var onlineByKey = online.SelectMany(player => Keys(player.PlayerUid, player.UserId).Select(key => (key, player)))
            .GroupBy(static pair => pair.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().player, StringComparer.OrdinalIgnoreCase);
        var identities = disciplineDashboard?.Identities.ToDictionary(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
                         ?? new Dictionary<string, DisciplineIdentity>(StringComparer.OrdinalIgnoreCase);
        var violations = disciplineDashboard?.Violations.GroupBy(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase)
                         ?? new Dictionary<string, DisciplineViolation[]>(StringComparer.OrdinalIgnoreCase);
        var bans = disciplineDashboard?.BanEntries ?? [];
        var whitelist = disciplineDashboard?.WhitelistEntries ?? [];
        var now = DateTimeOffset.UtcNow;

        var players = snapshot.Players.Select(player =>
        {
            var playerKeys = Keys(player.PlayerUid, player.UserId).ToArray();
            var live = playerKeys.Select(key => onlineByKey.GetValueOrDefault(key)).FirstOrDefault(static item => item is not null);
            var isOnline = live?.Online == true;
            identities.TryGetValue(player.UserId, out var identity);
            violations.TryGetValue(player.UserId, out var playerViolations);
            playerViolations ??= [];
            var banned = bans.Any(item => !item.Expired && (Matches(item.Identifier, player.UserId) || Matches(item.Identifier, player.PlayerUid)));
            var whitelisted = whitelist.Any(item => Matches(item.UserId, player.UserId));
            var repeatedIdentity = identity is not null && (identity.IpAddresses.Count > 3 || identity.Names.Count > 3 || identity.PlayerUids.Count > 2);
            var suspiciousActivity = playerViolations.Any(item => item.Severity is "critical" or "high");
            var risk = PlayerRiskScorer.Score(playerViolations.Length, banned, repeatedIdentity, suspiciousActivity);
            var signals = new List<string>();
            if (playerViolations.Length > 0) signals.Add($"{playerViolations.Length} 条纪律记录");
            if (banned) signals.Add("当前存在封禁记录");
            if (repeatedIdentity) signals.Add("身份关联变化频繁");
            if (suspiciousActivity) signals.Add("存在高严重度记录");
            if (!isOnline && player.LastSeenAt.HasValue && now - player.LastSeenAt.Value > TimeSpan.FromDays(30)) signals.Add("超过 30 天未活动");
            var note = playerKeys.Select(key => state.PlayerNotes.GetValueOrDefault(key)).FirstOrDefault(static item => item is not null)?.Notes ?? string.Empty;
            return new PlayerInsightRecord(
                player.PlayerUid,
                player.UserId,
                string.IsNullOrWhiteSpace(player.Name) ? live?.Name ?? "未命名玩家" : player.Name,
                player.GuildName ?? live?.GuildName ?? string.Empty,
                player.Level,
                isOnline,
                player.LastSeenAt,
                playerViolations.Length,
                banned,
                whitelisted,
                risk,
                PlayerRiskScorer.Level(risk),
                signals,
                note,
                true);
        }).OrderByDescending(static item => item.Online).ThenByDescending(static item => item.RiskScore).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var known = new HashSet<string>(players.SelectMany(item => Keys(item.PlayerUid, item.UserId)), StringComparer.OrdinalIgnoreCase);
        var onlineOnlyPlayers = online.Where(item => Keys(item.PlayerUid, item.UserId).All(key => !known.Contains(key)))
            .Select(player => BuildOnlineOnly(player, state));
        var combined = players.Concat(onlineOnlyPlayers).ToArray();
        return new(
            combined.Length,
            combined.Count(static item => item.Online),
            combined.Count(static item => item.RiskScore >= 70),
            combined.Count(item => !item.Online && item.LastSeenAt.HasValue && now - item.LastSeenAt.Value > TimeSpan.FromDays(30)),
            snapshot.ParsedAt,
            combined,
            warnings.Concat(disciplineDashboard?.Warnings ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray(),
            now);
    }

    public Task<PlayerInsightNote> UpdateNoteAsync(string playerKey, PlayerInsightNoteWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var key = validator.ValidateName(playerKey, nameof(playerKey), 160);
        var note = validator.LimitText(request.Notes, 2000);
        var record = new PlayerInsightNote(key, note, actor, DateTimeOffset.UtcNow);
        return repository.MutateAsync(state =>
        {
            if (string.IsNullOrWhiteSpace(note)) state.PlayerNotes.Remove(key);
            else state.PlayerNotes[key] = record;
            return record;
        }, cancellationToken);
    }

    private static PlayerInsightRecord BuildOnlineOnly(PalOps.Web.Contracts.PlayerResponse player, AdvancedOperationsStateDocument state)
    {
        var note = Keys(player.PlayerUid, player.UserId).Select(key => state.PlayerNotes.GetValueOrDefault(key)).FirstOrDefault(static item => item is not null)?.Notes ?? string.Empty;
        return new(
            player.PlayerUid,
            player.UserId,
            string.IsNullOrWhiteSpace(player.Name) ? "未命名玩家" : player.Name,
            player.GuildName,
            player.Level ?? 0,
            true,
            DateTimeOffset.UtcNow,
            0,
            false,
            false,
            0,
            "none",
            ["仅在线接口数据，尚未进入存档索引"],
            note,
            true);
    }

    private static IEnumerable<string> Keys(string? playerUid, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId)) yield return userId.Trim();
        if (!string.IsNullOrWhiteSpace(playerUid)) yield return playerUid.Trim();
    }

    private static bool Matches(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static string Limit(string value) => value.Length <= 300 ? value : value[..300];
}
