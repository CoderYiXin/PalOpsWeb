using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.AdvancedOperations;

public interface IWorldGovernanceService
{
    Task<WorldGovernanceDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<GovernanceReview> ReviewAsync(string candidateId, GovernanceReviewWriteRequest request, string actor, CancellationToken cancellationToken = default);
}

public sealed class WorldGovernanceService(
    IAdvancedOperationsRepository repository,
    ISaveIndexRepository saveIndex,
    AdvancedOperationsValidator validator) : IWorldGovernanceService
{
    public async Task<WorldGovernanceDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var stateTask = repository.ReadAsync(cancellationToken);
        var snapshot = await saveIndex.GetCurrentAsync(cancellationToken);
        var state = await stateTask;
        if (snapshot is null)
            return new(0, 0, 0, 0, null, [], ["尚无可用存档索引，无法分析公会与据点。"], DateTimeOffset.UtcNow);

        var candidates = new List<WorldGovernanceCandidate>();
        var guildById = snapshot.Guilds.Where(static item => !string.IsNullOrWhiteSpace(item.GuildId))
            .GroupBy(static item => item.GuildId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var guild in snapshot.Guilds)
        {
            if (guild.MemberPlayerUids.Count == 0)
                candidates.Add(Create("empty-guild", IncidentSeverity.High, $"空公会：{Display(guild.Name, guild.GuildId)}", "公会没有任何成员，建议人工核对是否为遗留数据。", guild.GuildId, string.Empty, 0, 0, guild.LastActivityAt, state));
            if (!guild.LastActivityAt.HasValue || now - guild.LastActivityAt.Value > TimeSpan.FromDays(30))
                candidates.Add(Create("inactive-guild", IncidentSeverity.Medium, $"长期不活跃公会：{Display(guild.Name, guild.GuildId)}", "公会超过 30 天无活动记录；仅作为清理候选，不自动删除。", guild.GuildId, string.Empty, guild.MemberPlayerUids.Count, 0, guild.LastActivityAt, state));
            if (guild.BaseIds.Count > 4)
                candidates.Add(Create("dense-guild-bases", IncidentSeverity.Low, $"据点密度较高：{Display(guild.Name, guild.GuildId)}", $"该公会关联 {guild.BaseIds.Count} 个据点，请核对服务器规则。", guild.GuildId, string.Empty, guild.MemberPlayerUids.Count, 0, guild.LastActivityAt, state));
        }

        foreach (var baseCamp in snapshot.Bases)
        {
            if (string.IsNullOrWhiteSpace(baseCamp.GuildId) || !guildById.ContainsKey(baseCamp.GuildId))
                candidates.Add(Create("orphan-base", IncidentSeverity.High, $"孤儿据点：{baseCamp.BaseId}", "据点无法关联到当前公会记录。", baseCamp.GuildId, baseCamp.BaseId, 0, baseCamp.WorkerCount, null, state));
            if (!baseCamp.PositionResolved)
                candidates.Add(Create("unlocated-base", IncidentSeverity.Low, $"未定位据点：{baseCamp.BaseId}", baseCamp.AssociationReason ?? "据点坐标未解析。", baseCamp.GuildId, baseCamp.BaseId, 0, baseCamp.WorkerCount, null, state));
            if (baseCamp.WorkerCount == 0 && baseCamp.MapObjectCount == 0)
                candidates.Add(Create("empty-base", IncidentSeverity.Medium, $"空据点：{baseCamp.BaseId}", "据点没有工作帕鲁或地图对象，可能为残留记录。", baseCamp.GuildId, baseCamp.BaseId, 0, 0, null, state));
        }

        var distinct = candidates.GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).Select(static group => group.First())
            .OrderByDescending(static item => item.Severity == IncidentSeverity.High)
            .ThenBy(static item => item.ReviewStatus)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(snapshot.Guilds.Count, snapshot.Bases.Count, distinct.Length,
            distinct.Count(static item => item.Severity is IncidentSeverity.Critical or IncidentSeverity.High),
            snapshot.ParsedAt, distinct, [], now);
    }

    public Task<GovernanceReview> ReviewAsync(string candidateId, GovernanceReviewWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var id = validator.ValidateName(candidateId, nameof(candidateId), 240);
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (status is not ("pending" or "reviewed" or "ignored"))
            throw new ArgumentException("Review status must be pending, reviewed, or ignored.", nameof(request.Status));
        var review = new GovernanceReview(id, status, validator.LimitText(request.Note, 1000), actor, DateTimeOffset.UtcNow);
        return repository.MutateAsync(state =>
        {
            if (status == "pending" && string.IsNullOrWhiteSpace(review.Note)) state.GovernanceReviews.Remove(id);
            else state.GovernanceReviews[id] = review;
            return review;
        }, cancellationToken);
    }

    private static WorldGovernanceCandidate Create(
        string type,
        string severity,
        string title,
        string description,
        string? guildId,
        string? baseId,
        int memberCount,
        int workerCount,
        DateTimeOffset? lastActivity,
        AdvancedOperationsStateDocument state)
    {
        var id = $"{type}:{guildId}:{baseId}".ToLowerInvariant();
        state.GovernanceReviews.TryGetValue(id, out var review);
        return new(
            id,
            type,
            severity,
            title,
            description,
            guildId ?? string.Empty,
            baseId ?? string.Empty,
            memberCount,
            workerCount,
            lastActivity,
            review?.Status ?? "pending",
            review?.Note ?? string.Empty,
            review?.ReviewedBy ?? string.Empty,
            review?.ReviewedAt,
            true);
    }

    private static string Display(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;
}
