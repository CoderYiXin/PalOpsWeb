using PalOps.Web.Contracts;
using PalOps.Web.Players;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.Endpoints;

public static class GuildEndpoints
{
    public static IEndpointRouteBuilder MapGuildEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/guilds").RequireAuthorization().WithTags("Guilds");

        group.MapGet("", async (
            string? query,
            ISaveIndexRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await repository.GetCurrentAsync(cancellationToken);
            if (snapshot is null)
                return Results.Ok(new ApiResponse<IReadOnlyList<GuildSummaryV1>>([], context.TraceIdentifier, [new ApiWarning("SAVE_INDEX_UNAVAILABLE", "尚无可用存档快照。")]));
            var needle = query?.Trim();
            var players = snapshot.Players.ToDictionary(x => x.PlayerUid, StringComparer.OrdinalIgnoreCase);
            var data = snapshot.Guilds
                .Where(guild => string.IsNullOrWhiteSpace(needle) ||
                    guild.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    guild.GuildId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    guild.MemberPlayerUids.Any(uid => players.TryGetValue(uid, out var player) && player.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .Select(guild => new GuildSummaryV1(
                    guild.GuildId,
                    guild.Name,
                    guild.LeaderPlayerUid,
                    guild.Level,
                    guild.MemberPlayerUids.Count,
                    Math.Max(
                        guild.BaseIds.Count,
                        snapshot.Bases.Count(baseCamp => baseCamp.GuildId.Equals(guild.GuildId, StringComparison.OrdinalIgnoreCase))),
                    guild.LastActivityAt,
                    snapshot.ParsedAt))
                .OrderByDescending(x => x.MemberCount)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Results.Ok(new ApiResponse<IReadOnlyList<GuildSummaryV1>>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/bases/unresolved", async (
            ISaveIndexRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await RequireSnapshotAsync(repository, cancellationToken);
            var guilds = snapshot.Guilds.ToDictionary(x => x.GuildId, x => x.Name, StringComparer.OrdinalIgnoreCase);
            var data = snapshot.Bases
                .Where(baseCamp => baseCamp.AssociationType == "unresolved" || !baseCamp.PositionResolved)
                .Select(baseCamp => ToContract(baseCamp, guilds.GetValueOrDefault(baseCamp.GuildId, string.Empty), snapshot.ParsedAt))
                .OrderBy(x => x.BaseId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Results.Ok(new ApiResponse<IReadOnlyList<BaseCampV1>>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/{guildId}", async (
            string guildId,
            ISaveIndexRepository repository,
            IPlayerAggregationService livePlayers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await RequireSnapshotAsync(repository, cancellationToken);
            var guild = RequireGuild(snapshot, guildId);
            var onlineKeys = await TryGetOnlineKeysAsync(livePlayers, cancellationToken);
            var members = ProjectMembers(snapshot, guild, onlineKeys);
            var bases = ProjectBases(snapshot, guild);
            var leaderName = members.FirstOrDefault(x => x.Leader)?.Name ?? string.Empty;
            var detail = new GuildDetailV1(
                guild.GuildId,
                guild.Name,
                guild.LeaderPlayerUid,
                leaderName,
                guild.Level,
                guild.LastActivityAt,
                snapshot.ParsedAt,
                members,
                bases);
            return Results.Ok(new ApiResponse<GuildDetailV1>(detail, context.TraceIdentifier, []));
        });

        group.MapGet("/{guildId}/members", async (
            string guildId,
            ISaveIndexRepository repository,
            IPlayerAggregationService livePlayers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await RequireSnapshotAsync(repository, cancellationToken);
            var guild = RequireGuild(snapshot, guildId);
            var members = ProjectMembers(snapshot, guild, await TryGetOnlineKeysAsync(livePlayers, cancellationToken));
            return Results.Ok(new ApiResponse<IReadOnlyList<GuildMemberV1>>(members, context.TraceIdentifier, []));
        });

        group.MapGet("/{guildId}/bases", async (
            string guildId,
            ISaveIndexRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await RequireSnapshotAsync(repository, cancellationToken);
            var guild = RequireGuild(snapshot, guildId);
            return Results.Ok(new ApiResponse<IReadOnlyList<BaseCampV1>>(ProjectBases(snapshot, guild), context.TraceIdentifier, []));
        });

        group.MapGet("/bases/{baseId}", async (
            string baseId,
            ISaveIndexRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await RequireSnapshotAsync(repository, cancellationToken);
            var baseCamp = snapshot.Bases.FirstOrDefault(x => x.BaseId.Equals(baseId, StringComparison.OrdinalIgnoreCase))
                           ?? throw new KeyNotFoundException("据点不存在。");
            var guild = snapshot.Guilds.FirstOrDefault(x => x.GuildId.Equals(baseCamp.GuildId, StringComparison.OrdinalIgnoreCase));
            var data = ToContract(baseCamp, guild?.Name ?? string.Empty, snapshot.ParsedAt);
            return Results.Ok(new ApiResponse<BaseCampV1>(data, context.TraceIdentifier, []));
        });

        return endpoints;
    }

    private static async Task<SaveIndexSnapshot> RequireSnapshotAsync(ISaveIndexRepository repository, CancellationToken cancellationToken)
        => await repository.GetCurrentAsync(cancellationToken) ?? throw new PalOps.Web.SaveGames.SaveIndexUnavailableException("尚无可用存档快照。");

    private static IndexedGuild RequireGuild(SaveIndexSnapshot snapshot, string guildId)
        => snapshot.Guilds.FirstOrDefault(candidate => candidate.GuildId.Equals(guildId, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException("公会不存在。");

    private static IReadOnlyList<GuildMemberV1> ProjectMembers(
        SaveIndexSnapshot snapshot,
        IndexedGuild guild,
        IReadOnlySet<string> onlineKeys)
    {
        return snapshot.Players
            .Where(player => guild.MemberPlayerUids.Contains(player.PlayerUid, StringComparer.OrdinalIgnoreCase))
            .Select(player => new GuildMemberV1(
                player.PlayerUid,
                player.UserId,
                player.Name,
                player.Level,
                onlineKeys.Contains(player.PlayerUid) || onlineKeys.Contains(player.UserId),
                string.Equals(guild.LeaderPlayerUid, player.PlayerUid, StringComparison.OrdinalIgnoreCase),
                player.LastSeenAt))
            .OrderByDescending(x => x.Leader)
            .ThenByDescending(x => x.Online)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<BaseCampV1> ProjectBases(SaveIndexSnapshot snapshot, IndexedGuild guild)
        => snapshot.Bases
            .Where(baseCamp => baseCamp.GuildId.Equals(guild.GuildId, StringComparison.OrdinalIgnoreCase)
                               || guild.BaseIds.Contains(baseCamp.BaseId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(baseCamp => baseCamp.BaseId, StringComparer.OrdinalIgnoreCase)
            .Select(group => ToContract(group.First(), guild.Name, snapshot.ParsedAt))
            .OrderBy(x => x.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static BaseCampV1 ToContract(IndexedBaseCamp baseCamp, string guildName, DateTimeOffset snapshotAt)
        => new(
            baseCamp.BaseId,
            baseCamp.GuildId,
            guildName,
            baseCamp.X,
            baseCamp.Y,
            baseCamp.Z,
            baseCamp.WorkerCount,
            baseCamp.MapObjectCount,
            snapshotAt,
            baseCamp.AssociationType,
            baseCamp.PositionSource,
            baseCamp.RelatedPlayerUids,
            baseCamp.AssociationReason,
            baseCamp.PositionResolved,
            "base:" + baseCamp.BaseId);

    private static async Task<IReadOnlySet<string>> TryGetOnlineKeysAsync(IPlayerAggregationService service, CancellationToken cancellationToken)
    {
        try
        {
            var players = await service.GetOnlinePlayersAsync(cancellationToken);
            return players.SelectMany(x => new[] { x.PlayerUid, x.UserId })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }
}
