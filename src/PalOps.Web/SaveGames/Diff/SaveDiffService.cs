using PalOps.Web.Catalog;

namespace PalOps.Web.SaveGames.Diff;

public interface ISaveDiffService
{
    Task<IReadOnlyList<SaveDiffSnapshotSummary>> ListSnapshotsAsync(CancellationToken cancellationToken = default);
    Task<SaveDiffReport> CompareLatestAsync(int limit = 1_000, CancellationToken cancellationToken = default);
    Task<SaveDiffReport> CompareAsync(string fromSnapshotId, string toSnapshotId, int limit = 1_000, CancellationToken cancellationToken = default);
    Task<SaveDiffReport> CompareForExportAsync(string fromSnapshotId, string toSnapshotId, CancellationToken cancellationToken = default);
}

public sealed class SaveDiffService : ISaveDiffService
{
    private readonly ISaveChangeSnapshotRepository _repository;
    private readonly IGameNameLookup _names;
    private readonly TimeProvider _timeProvider;

    public SaveDiffService(
        ISaveChangeSnapshotRepository repository,
        IGameNameLookup names,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _names = names;
        _timeProvider = timeProvider;
    }

    internal const double BaseMovementThreshold = 250;
    private const int MaximumApiDetails = 5_000;
    private const int MaximumExportDetails = 50_000;

    public Task<IReadOnlyList<SaveDiffSnapshotSummary>> ListSnapshotsAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public async Task<SaveDiffReport> CompareLatestAsync(int limit = 1_000, CancellationToken cancellationToken = default)
    {
        var snapshots = await _repository.ListAsync(cancellationToken);
        if (snapshots.Count < 2)
            throw new SaveDiffValidationException("SAVE_DIFF_INSUFFICIENT_SNAPSHOTS", "至少需要两份成功解析快照才能比较。" );

        var latest = snapshots[0];
        var previous = snapshots.Skip(1)
            .FirstOrDefault(candidate => candidate.WorldId.Equals(latest.WorldId, StringComparison.OrdinalIgnoreCase));
        if (previous is null)
            throw new SaveDiffValidationException("SAVE_DIFF_INSUFFICIENT_SNAPSHOTS", "当前世界只有一份可用差异快照。" );

        return await CompareInternalAsync(previous.SnapshotId, latest.SnapshotId, NormalizeLimit(limit, MaximumApiDetails), cancellationToken);
    }

    public Task<SaveDiffReport> CompareAsync(
        string fromSnapshotId,
        string toSnapshotId,
        int limit = 1_000,
        CancellationToken cancellationToken = default)
        => CompareInternalAsync(fromSnapshotId, toSnapshotId, NormalizeLimit(limit, MaximumApiDetails), cancellationToken);

    public Task<SaveDiffReport> CompareForExportAsync(
        string fromSnapshotId,
        string toSnapshotId,
        CancellationToken cancellationToken = default)
        => CompareInternalAsync(fromSnapshotId, toSnapshotId, MaximumExportDetails, cancellationToken);

    private async Task<SaveDiffReport> CompareInternalAsync(
        string fromSnapshotId,
        string toSnapshotId,
        int limit,
        CancellationToken cancellationToken)
    {
        var fromId = fromSnapshotId?.Trim() ?? string.Empty;
        var toId = toSnapshotId?.Trim() ?? string.Empty;
        if (fromId.Length == 0 || toId.Length == 0)
            throw new SaveDiffValidationException("SAVE_DIFF_SNAPSHOT_REQUIRED", "必须选择源快照和目标快照。" );
        if (fromId.Equals(toId, StringComparison.OrdinalIgnoreCase))
            throw new SaveDiffValidationException("SAVE_DIFF_SAME_SNAPSHOT", "源快照和目标快照不能相同。" );

        var from = await _repository.GetAsync(fromId, cancellationToken)
                   ?? throw new SaveDiffValidationException("SAVE_DIFF_SNAPSHOT_NOT_FOUND", $"源快照不存在或已损坏：{fromId}");
        var to = await _repository.GetAsync(toId, cancellationToken)
                 ?? throw new SaveDiffValidationException("SAVE_DIFF_SNAPSHOT_NOT_FOUND", $"目标快照不存在或已损坏：{toId}");

        if (!from.WorldId.Equals(to.WorldId, StringComparison.OrdinalIgnoreCase))
            throw new SaveDiffValidationException("SAVE_DIFF_WORLD_MISMATCH", "只能比较同一个世界的快照。" );
        if (from.ParsedAt >= to.ParsedAt)
            throw new SaveDiffValidationException("SAVE_DIFF_ORDER_INVALID", "源快照必须严格早于目标快照。" );

        var players = ComparePlayers(from, to);
        var guilds = CompareGuilds(from, to);
        var bases = CompareBases(from, to);
        var items = CompareItems(from, to);
        var pals = ComparePals(from, to);
        var anomalies = DetectAnomalies(from, to, players, guilds, bases, items, pals);

        var summary = new SaveDiffSummary(
            players.Count(change => change.ChangeType == SaveDiffChangeKind.Added),
            players.Count(change => change.ChangeType == SaveDiffChangeKind.Removed),
            players.Count(change => change.ChangeType == SaveDiffChangeKind.Changed),
            guilds.Count,
            bases.Count,
            items.Count,
            items.Count(change => change.Important),
            pals.Count,
            anomalies.Count,
            anomalies.Count(anomaly => anomaly.Severity == SaveDiffSeverity.Critical));

        return new SaveDiffReport(
            JsonSaveChangeSnapshotRepository.ToSummary(from),
            JsonSaveChangeSnapshotRepository.ToSummary(to),
            _timeProvider.GetUtcNow(),
            summary,
            Limit(players, limit),
            Limit(guilds, limit),
            Limit(bases, limit),
            Limit(items, limit),
            Limit(pals, limit),
            anomalies);
    }

    private static IReadOnlyList<SaveDiffPlayerChange> ComparePlayers(SaveChangeSnapshot from, SaveChangeSnapshot to)
    {
        var before = from.Players.ToDictionary(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase);
        var after = to.Players.ToDictionary(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase);
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var changes = new List<SaveDiffPlayerChange>();

        foreach (var key in keys)
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            if (oldValue is null)
            {
                changes.Add(new SaveDiffPlayerChange(
                    SaveDiffChangeKind.Added, key, string.Empty, newValue!.Name, null, newValue.Level, newValue.Level,
                    null, newValue.Experience, newValue.Experience, string.Empty, newValue.GuildId, null,
                    ["player"]));
                continue;
            }
            if (newValue is null)
            {
                changes.Add(new SaveDiffPlayerChange(
                    SaveDiffChangeKind.Removed, key, oldValue.Name, string.Empty, oldValue.Level, null, -oldValue.Level,
                    oldValue.Experience, null, -oldValue.Experience, oldValue.GuildId, string.Empty, null,
                    ["player"]));
                continue;
            }

            var fields = new List<string>();
            if (!oldValue.Name.Equals(newValue.Name, StringComparison.Ordinal)) fields.Add("name");
            if (oldValue.Level != newValue.Level) fields.Add("level");
            if (oldValue.Experience != newValue.Experience) fields.Add("experience");
            if (!oldValue.GuildId.Equals(newValue.GuildId, StringComparison.OrdinalIgnoreCase)) fields.Add("guild");
            var distance = Distance(oldValue.X, oldValue.Y, oldValue.Z, newValue.X, newValue.Y, newValue.Z);
            if (distance is >= 1) fields.Add("position");
            if (fields.Count == 0) continue;

            changes.Add(new SaveDiffPlayerChange(
                SaveDiffChangeKind.Changed,
                key,
                oldValue.Name,
                newValue.Name,
                oldValue.Level,
                newValue.Level,
                newValue.Level - oldValue.Level,
                oldValue.Experience,
                newValue.Experience,
                newValue.Experience - oldValue.Experience,
                oldValue.GuildId,
                newValue.GuildId,
                distance,
                fields));
        }

        return changes
            .OrderBy(change => ChangeOrder(change.ChangeType))
            .ThenBy(change => FirstNonEmpty(change.AfterName, change.BeforeName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SaveDiffGuildChange> CompareGuilds(SaveChangeSnapshot from, SaveChangeSnapshot to)
    {
        var before = from.Guilds.ToDictionary(guild => guild.GuildId, StringComparer.OrdinalIgnoreCase);
        var after = to.Guilds.ToDictionary(guild => guild.GuildId, StringComparer.OrdinalIgnoreCase);
        var changes = new List<SaveDiffGuildChange>();

        foreach (var key in before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            if (oldValue is null)
            {
                changes.Add(new SaveDiffGuildChange(
                    SaveDiffChangeKind.Added, key, string.Empty, newValue!.Name, null, newValue.Level,
                    string.Empty, newValue.LeaderPlayerUid, 0, newValue.MemberPlayerUids.Count, newValue.MemberPlayerUids, [], ["guild"]));
                continue;
            }
            if (newValue is null)
            {
                changes.Add(new SaveDiffGuildChange(
                    SaveDiffChangeKind.Removed, key, oldValue.Name, string.Empty, oldValue.Level, null,
                    oldValue.LeaderPlayerUid, string.Empty, oldValue.MemberPlayerUids.Count, 0, [], oldValue.MemberPlayerUids, ["guild"]));
                continue;
            }

            var oldMembers = oldValue.MemberPlayerUids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newMembers = newValue.MemberPlayerUids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var addedMembers = newMembers.Except(oldMembers, StringComparer.OrdinalIgnoreCase).OrderBy(uid => uid, StringComparer.OrdinalIgnoreCase).ToArray();
            var removedMembers = oldMembers.Except(newMembers, StringComparer.OrdinalIgnoreCase).OrderBy(uid => uid, StringComparer.OrdinalIgnoreCase).ToArray();
            var fields = new List<string>();
            if (!oldValue.Name.Equals(newValue.Name, StringComparison.Ordinal)) fields.Add("name");
            if (oldValue.Level != newValue.Level) fields.Add("level");
            if (!oldValue.LeaderPlayerUid.Equals(newValue.LeaderPlayerUid, StringComparison.OrdinalIgnoreCase)) fields.Add("leader");
            if (addedMembers.Length > 0 || removedMembers.Length > 0) fields.Add("members");
            if (fields.Count == 0) continue;

            changes.Add(new SaveDiffGuildChange(
                SaveDiffChangeKind.Changed, key, oldValue.Name, newValue.Name, oldValue.Level, newValue.Level,
                oldValue.LeaderPlayerUid, newValue.LeaderPlayerUid, oldValue.MemberPlayerUids.Count, newValue.MemberPlayerUids.Count, addedMembers, removedMembers, fields));
        }

        return changes
            .OrderBy(change => ChangeOrder(change.ChangeType))
            .ThenBy(change => FirstNonEmpty(change.AfterName, change.BeforeName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.GuildId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SaveDiffBaseChange> CompareBases(SaveChangeSnapshot from, SaveChangeSnapshot to)
    {
        var before = from.Bases.ToDictionary(baseCamp => baseCamp.BaseId, StringComparer.OrdinalIgnoreCase);
        var after = to.Bases.ToDictionary(baseCamp => baseCamp.BaseId, StringComparer.OrdinalIgnoreCase);
        var changes = new List<SaveDiffBaseChange>();

        foreach (var key in before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            if (oldValue is null)
            {
                changes.Add(new SaveDiffBaseChange(
                    SaveDiffChangeKind.Added, key, string.Empty, newValue!.GuildId,
                    null, null, null, newValue.X, newValue.Y, newValue.Z, null,
                    null, newValue.WorkerCount, null, newValue.MapObjectCount, ["base"]));
                continue;
            }
            if (newValue is null)
            {
                changes.Add(new SaveDiffBaseChange(
                    SaveDiffChangeKind.Removed, key, oldValue.GuildId, string.Empty,
                    oldValue.X, oldValue.Y, oldValue.Z, null, null, null, null,
                    oldValue.WorkerCount, null, oldValue.MapObjectCount, null, ["base"]));
                continue;
            }

            var fields = new List<string>();
            if (!oldValue.GuildId.Equals(newValue.GuildId, StringComparison.OrdinalIgnoreCase)) fields.Add("guild");
            if (oldValue.WorkerCount != newValue.WorkerCount) fields.Add("workers");
            if (oldValue.MapObjectCount != newValue.MapObjectCount) fields.Add("mapObjects");
            if (!oldValue.AssociationType.Equals(newValue.AssociationType, StringComparison.OrdinalIgnoreCase)) fields.Add("association");
            var distance = Distance(oldValue.X, oldValue.Y, oldValue.Z, newValue.X, newValue.Y, newValue.Z);
            if (distance is >= BaseMovementThreshold) fields.Add("position");
            if (fields.Count == 0) continue;

            changes.Add(new SaveDiffBaseChange(
                SaveDiffChangeKind.Changed, key, oldValue.GuildId, newValue.GuildId,
                oldValue.X, oldValue.Y, oldValue.Z, newValue.X, newValue.Y, newValue.Z, distance,
                oldValue.WorkerCount, newValue.WorkerCount, oldValue.MapObjectCount, newValue.MapObjectCount, fields));
        }

        return changes
            .OrderBy(change => ChangeOrder(change.ChangeType))
            .ThenBy(change => change.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<SaveDiffItemChange> CompareItems(SaveChangeSnapshot from, SaveChangeSnapshot to)
    {
        var before = from.Items.ToDictionary(ItemKey.From, ItemKeyComparer.Instance);
        var after = to.Items.ToDictionary(ItemKey.From, ItemKeyComparer.Instance);
        var playerNames = PlayerNames(from, to);
        var changes = new List<SaveDiffItemChange>();

        foreach (var key in before.Keys.Concat(after.Keys).Distinct(ItemKeyComparer.Instance))
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            var oldQuantity = oldValue?.Quantity ?? 0;
            var newQuantity = newValue?.Quantity ?? 0;
            if (oldQuantity == newQuantity) continue;
            var delta = newQuantity - oldQuantity;
            var changeType = oldValue is null ? SaveDiffChangeKind.Added : newValue is null ? SaveDiffChangeKind.Removed : SaveDiffChangeKind.Changed;
            changes.Add(new SaveDiffItemChange(
                changeType,
                key.PlayerUid,
                playerNames.GetValueOrDefault(key.PlayerUid, key.PlayerUid),
                key.ContainerType,
                key.ItemId,
                _names.Resolve("items", key.ItemId),
                key.Quality,
                key.DynamicItemId,
                oldQuantity,
                newQuantity,
                delta,
                (oldValue?.Important ?? false) || (newValue?.Important ?? false) || Math.Abs(delta) >= 100));
        }

        return changes
            .OrderByDescending(change => change.Important)
            .ThenBy(change => ChangeOrder(change.ChangeType))
            .ThenBy(change => change.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.ContainerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Quality)
            .ToArray();
    }

    private IReadOnlyList<SaveDiffPalChange> ComparePals(SaveChangeSnapshot from, SaveChangeSnapshot to)
    {
        var before = from.Pals.ToDictionary(PalKey.From, PalKeyComparer.Instance);
        var after = to.Pals.ToDictionary(PalKey.From, PalKeyComparer.Instance);
        var playerNames = PlayerNames(from, to);
        var changes = new List<SaveDiffPalChange>();

        foreach (var key in before.Keys.Concat(after.Keys).Distinct(PalKeyComparer.Instance))
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            if (oldValue is not null && newValue is not null
                && oldValue.Count == newValue.Count
                && oldValue.LuckyCount == newValue.LuckyCount
                && oldValue.BossCount == newValue.BossCount
                && Math.Abs(oldValue.AverageLevel - newValue.AverageLevel) < 0.01)
                continue;

            var oldCount = oldValue?.Count ?? 0;
            var newCount = newValue?.Count ?? 0;
            changes.Add(new SaveDiffPalChange(
                oldValue is null ? SaveDiffChangeKind.Added : newValue is null ? SaveDiffChangeKind.Removed : SaveDiffChangeKind.Changed,
                key.PlayerUid,
                playerNames.GetValueOrDefault(key.PlayerUid, key.PlayerUid),
                key.PalId,
                _names.Resolve("pals", key.PalId),
                oldCount,
                newCount,
                newCount - oldCount,
                oldValue?.LuckyCount ?? 0,
                newValue?.LuckyCount ?? 0,
                oldValue?.BossCount ?? 0,
                newValue?.BossCount ?? 0,
                oldValue?.AverageLevel ?? 0,
                newValue?.AverageLevel ?? 0));
        }

        return changes
            .OrderBy(change => ChangeOrder(change.ChangeType))
            .ThenBy(change => change.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.PalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SaveDiffAnomaly> DetectAnomalies(
        SaveChangeSnapshot from,
        SaveChangeSnapshot to,
        IReadOnlyList<SaveDiffPlayerChange> playerChanges,
        IReadOnlyList<SaveDiffGuildChange> guildChanges,
        IReadOnlyList<SaveDiffBaseChange> baseChanges,
        IReadOnlyList<SaveDiffItemChange> itemChanges,
        IReadOnlyList<SaveDiffPalChange> palChanges)
    {
        var anomalies = new List<SaveDiffAnomaly>();
        var playerDrop = from.Players.Count - to.Players.Count;
        var playerDropPercent = PercentDrop(from.Players.Count, to.Players.Count);
        if (playerDrop >= 5 && playerDropPercent >= 20)
            anomalies.Add(Anomaly(
                playerDropPercent >= 50 ? SaveDiffSeverity.Critical : SaveDiffSeverity.Warning,
                "PlayerCountDrop", "players", from.WorldId, "玩家总数大幅下降",
                $"玩家数量由 {from.Players.Count} 下降到 {to.Players.Count}，减少 {playerDrop} 人（{playerDropPercent:F1}%）。",
                from.Players.Count, to.Players.Count, playerDropPercent));

        foreach (var change in playerChanges.Where(change => change.ChangeType == SaveDiffChangeKind.Changed && change.LevelDelta < 0))
            anomalies.Add(Anomaly(
                SaveDiffSeverity.Warning, "PlayerLevelDecrease", "players", change.PlayerUid, "玩家等级下降",
                $"{FirstNonEmpty(change.AfterName, change.BeforeName, change.PlayerUid)} 的等级由 {change.BeforeLevel} 降到 {change.AfterLevel}。",
                change.BeforeLevel, change.AfterLevel, PercentDrop(change.BeforeLevel ?? 0, change.AfterLevel ?? 0)));

        foreach (var change in guildChanges.Where(change => change.ChangeType != SaveDiffChangeKind.Added))
        {
            var removed = change.RemovedMemberPlayerUids.Count;
            var dropPercent = PercentDrop(change.BeforeMemberCount, change.AfterMemberCount);
            if (removed >= 3 && dropPercent >= 50)
                anomalies.Add(Anomaly(
                    SaveDiffSeverity.Warning, "GuildMemberDrop", "guilds", change.GuildId, "公会成员大幅减少",
                    $"公会 {FirstNonEmpty(change.AfterName, change.BeforeName, change.GuildId)} 的成员由 {change.BeforeMemberCount} 减少到 {change.AfterMemberCount}（{dropPercent:F1}%）。",
                    change.BeforeMemberCount, change.AfterMemberCount, dropPercent));
        }

        var removedBases = baseChanges.Count(change => change.ChangeType == SaveDiffChangeKind.Removed);
        var baseDropPercent = PercentDrop(from.Bases.Count, to.Bases.Count);
        if (removedBases >= 3 || (removedBases > 0 && baseDropPercent >= 20))
            anomalies.Add(Anomaly(
                removedBases >= 3 && baseDropPercent >= 50 ? SaveDiffSeverity.Critical : SaveDiffSeverity.Warning,
                "BaseCountDrop", "bases", from.WorldId, "据点数量明显下降",
                $"据点数量由 {from.Bases.Count} 下降到 {to.Bases.Count}，消失 {removedBases} 个（{baseDropPercent:F1}%）。",
                from.Bases.Count, to.Bases.Count, baseDropPercent));

        var afterItemTotals = to.Items
            .GroupBy(item => item.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.OrdinalIgnoreCase);
        foreach (var group in from.Items.GroupBy(item => item.PlayerUid, StringComparer.OrdinalIgnoreCase))
        {
            var beforeTotal = group.Sum(item => item.Quantity);
            var afterTotal = afterItemTotals.GetValueOrDefault(group.Key);
            var drop = beforeTotal - afterTotal;
            var dropPercent = PercentDrop(beforeTotal, afterTotal);
            if (drop >= 20 && dropPercent >= 50)
                anomalies.Add(Anomaly(
                    dropPercent >= 80 ? SaveDiffSeverity.Critical : SaveDiffSeverity.Warning,
                    "PlayerItemDrop", "items", group.Key, "玩家物品总量大幅下降",
                    $"玩家 {group.Key} 的物品总量由 {beforeTotal} 下降到 {afterTotal}，减少 {drop}（{dropPercent:F1}%）。",
                    beforeTotal, afterTotal, dropPercent));
        }

        foreach (var change in itemChanges.Where(change => change.Important && change.QuantityDelta <= -10))
            anomalies.Add(Anomaly(
                change.QuantityDelta <= -100 ? SaveDiffSeverity.Critical : SaveDiffSeverity.Warning,
                "ImportantItemDrop", "items", $"{change.PlayerUid}:{change.ItemId}", "重要物品数量下降",
                $"{change.PlayerName} 的 {change.ItemName} 由 {change.BeforeQuantity} 下降到 {change.AfterQuantity}。",
                change.BeforeQuantity, change.AfterQuantity, PercentDrop(change.BeforeQuantity, change.AfterQuantity)));

        var afterPalTotals = to.Pals
            .GroupBy(pal => pal.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(pal => pal.Count), StringComparer.OrdinalIgnoreCase);
        foreach (var group in from.Pals.GroupBy(pal => pal.PlayerUid, StringComparer.OrdinalIgnoreCase))
        {
            var beforeTotal = group.Sum(pal => pal.Count);
            var afterTotal = afterPalTotals.GetValueOrDefault(group.Key);
            var drop = beforeTotal - afterTotal;
            var dropPercent = PercentDrop(beforeTotal, afterTotal);
            if (drop >= 5 && dropPercent >= 50)
                anomalies.Add(Anomaly(
                    dropPercent >= 80 ? SaveDiffSeverity.Critical : SaveDiffSeverity.Warning,
                    "PalCountDrop", "pals", group.Key, "玩家帕鲁数量大幅下降",
                    $"玩家 {group.Key} 的帕鲁总数由 {beforeTotal} 下降到 {afterTotal}，减少 {drop}（{dropPercent:F1}%）。",
                    beforeTotal, afterTotal, dropPercent));
        }

        return anomalies
            .OrderBy(anomaly => SeverityOrder(anomaly.Severity))
            .ThenBy(anomaly => anomaly.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(anomaly => anomaly.EntityId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(anomaly => anomaly.Rule, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SaveDiffAnomaly Anomaly(
        SaveDiffSeverity severity,
        string rule,
        string category,
        string entityId,
        string title,
        string description,
        double? before,
        double? after,
        double? changePercent)
        => new(severity, rule, category, entityId, title, description, before, after, changePercent);

    private static IReadOnlyDictionary<string, string> PlayerNames(SaveChangeSnapshot from, SaveChangeSnapshot to)
        => from.Players.Concat(to.Players)
            .GroupBy(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(player => player.Name.Length).First().Name,
                StringComparer.OrdinalIgnoreCase);

    private static SaveDiffCollection<T> Limit<T>(IReadOnlyList<T> items, int limit)
        => new(items.Take(limit).ToArray(), items.Count, items.Count > limit);

    private static int NormalizeLimit(int limit, int maximum)
        => Math.Clamp(limit, 1, maximum);

    private static double? Distance(
        double? beforeX, double? beforeY, double? beforeZ,
        double? afterX, double? afterY, double? afterZ)
    {
        if (!beforeX.HasValue || !beforeY.HasValue || !afterX.HasValue || !afterY.HasValue) return null;
        var dx = afterX.Value - beforeX.Value;
        var dy = afterY.Value - beforeY.Value;
        var dz = (afterZ ?? 0) - (beforeZ ?? 0);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double PercentDrop(double before, double after)
        => before <= 0 || after >= before ? 0 : (before - after) / before * 100;

    private static int ChangeOrder(SaveDiffChangeKind kind)
        => kind switch { SaveDiffChangeKind.Removed => 0, SaveDiffChangeKind.Added => 1, _ => 2 };

    private static int SeverityOrder(SaveDiffSeverity severity)
        => severity switch { SaveDiffSeverity.Critical => 0, SaveDiffSeverity.Warning => 1, _ => 2 };

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private readonly record struct ItemKey(string PlayerUid, string ContainerType, string ItemId, int Quality, string DynamicItemId)
    {
        public static ItemKey From(SaveChangeItem item)
            => new(item.PlayerUid, item.ContainerType, item.ItemId, item.Quality, item.DynamicItemId);
    }

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

    private readonly record struct PalKey(string PlayerUid, string PalId)
    {
        public static PalKey From(SaveChangePal pal) => new(pal.PlayerUid, pal.PalId);
    }

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
