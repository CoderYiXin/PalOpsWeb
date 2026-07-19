using System.Globalization;
using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.SaveGames.Projection;

public sealed record ReconciledBaseCamp(IndexedBaseCamp BaseCamp, IndexedMapMarker? Marker);

public interface IGuildBaseReconciliationService
{
    IReadOnlyList<ReconciledBaseCamp> Reconcile(
        IReadOnlyList<BaseCampProjectionCandidate> candidates,
        IReadOnlyDictionary<string, IndexedGuild> guilds,
        IReadOnlyDictionary<string, WorldPlayerProfile> playerProfiles,
        DateTimeOffset sourceAt);
}

public sealed class GuildBaseReconciliationService : IGuildBaseReconciliationService
{
    public IReadOnlyList<ReconciledBaseCamp> Reconcile(
        IReadOnlyList<BaseCampProjectionCandidate> candidates,
        IReadOnlyDictionary<string, IndexedGuild> guilds,
        IReadOnlyDictionary<string, WorldPlayerProfile> playerProfiles,
        DateTimeOffset sourceAt)
    {
        var guildByBaseId = guilds.Values
            .SelectMany(guild => guild.BaseIds.Select(baseId => (baseId, guild.GuildId)))
            .GroupBy(x => x.baseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(v => v.GuildId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var guildByPlayerUid = playerProfiles.Values
            .Where(player => !string.IsNullOrWhiteSpace(player.GuildId))
            .GroupBy(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(v => v.GuildId!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);

        return candidates.Select(candidate => ReconcileOne(candidate, guilds, guildByBaseId, guildByPlayerUid, sourceAt)).ToArray();
    }

    private static ReconciledBaseCamp ReconcileOne(
        BaseCampProjectionCandidate candidate,
        IReadOnlyDictionary<string, IndexedGuild> guilds,
        IReadOnlyDictionary<string, string[]> guildByBaseId,
        IReadOnlyDictionary<string, string[]> guildByPlayerUid,
        DateTimeOffset sourceAt)
    {
        var reasons = new List<string>();
        var associationType = "unresolved";
        var guildId = string.Empty;

        if (!string.IsNullOrWhiteSpace(candidate.DirectGuildId) && guilds.ContainsKey(candidate.DirectGuildId))
        {
            guildId = candidate.DirectGuildId;
            associationType = "direct";
            reasons.Add("据点直接公会字段确认归属。");
        }
        else if (guildByBaseId.TryGetValue(candidate.BaseId, out var listedGuilds) && listedGuilds.Length == 1)
        {
            guildId = listedGuilds[0];
            associationType = "guild-list";
            reasons.Add("公会 BaseIds 列表确认归属。");
        }
        else
        {
            var memberGuilds = candidate.RelatedPlayerUids
                .Where(guildByPlayerUid.ContainsKey)
                .SelectMany(uid => guildByPlayerUid[uid])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (memberGuilds.Length == 1)
            {
                guildId = memberGuilds[0];
                associationType = "member";
                reasons.Add("据点关联成员所属公会推断归属。");
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.DirectGuildId) && !candidate.DirectGuildId.Equals(guildId, StringComparison.OrdinalIgnoreCase))
            reasons.Add($"直接字段 {candidate.DirectGuildId} 未能作为有效公会使用。");

        var position = candidate.DirectPosition ?? candidate.LinkedObjectPosition;
        var positionSource = candidate.DirectPosition.HasValue ? candidate.DirectPositionSource : candidate.LinkedObjectPosition.HasValue ? "object-link" : "unresolved";
        if (positionSource == "raw-data") reasons.Add("坐标由 BaseCamp RawData 稳定前缀解析。");
        if (positionSource == "object-link") reasons.Add("坐标由关联地图对象回填。");
        if (position is null) reasons.Add("未找到可用坐标，保留为待确认据点。");

        var indexed = new IndexedBaseCamp(
            candidate.BaseId,
            guildId,
            position?.X,
            position?.Y,
            position?.Z,
            candidate.WorkerCount,
            candidate.MapObjectCount)
        {
            AssociationType = associationType,
            PositionSource = positionSource,
            RelatedPlayerUids = candidate.RelatedPlayerUids,
            AssociationReason = string.Join(" ", reasons)
        };

        if (position is null) return new(indexed, null);
        var label = guilds.TryGetValue(guildId, out var guild) ? guild.Name : candidate.BaseId;
        var marker = new IndexedMapMarker(
            "base:" + candidate.BaseId,
            "guildBase",
            label,
            position.Value.X,
            position.Value.Y,
            position.Value.Z,
            "save",
            sourceAt,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["baseId"] = candidate.BaseId,
                ["guildId"] = guildId,
                ["associationType"] = associationType,
                ["positionSource"] = positionSource,
                ["workerCount"] = candidate.WorkerCount.ToString(CultureInfo.InvariantCulture),
                ["mapObjectCount"] = candidate.MapObjectCount.ToString(CultureInfo.InvariantCulture)
            });
        return new(indexed, marker);
    }
}
