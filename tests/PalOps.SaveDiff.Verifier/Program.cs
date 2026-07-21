using PalOps.Web.Catalog;
using PalOps.Web.SaveGames.Diff;
using PalOps.Web.SaveGames.Index;
using Microsoft.Extensions.Logging.Abstractions;

var failures = new List<string>();
Run("compact projection aggregates item slots and Pal species", VerifyProjectionAggregation);
Run("compact projection is deterministic", VerifyProjectionDeterminism);
Run("comparison covers players guilds bases items and Pals", () => VerifyComparisonAsync().GetAwaiter().GetResult());
Run("anomaly thresholds are explicit and deterministic", () => VerifyAnomaliesAsync().GetAwaiter().GetResult());
Run("repository retains thirty snapshots and quarantines corruption", () => VerifyRepositoryAsync().GetAwaiter().GetResult());
Run("repository rejects incompatible compact snapshot schema", () => VerifyRepositorySchemaAsync().GetAwaiter().GetResult());
Run("backfill isolates one damaged historical snapshot", () => VerifyBackfillIsolationAsync().GetAwaiter().GetResult());
Run("invalid snapshot pairs are rejected", () => VerifyInvalidPairsAsync().GetAwaiter().GetResult());
Run("CSV export neutralizes spreadsheet formulas", VerifyCsvSafety);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Save diff verifier failed ({failures.Count}):");
    foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("Save diff verifier passed.");
return 0;

void Run(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add($"{name}: {ex}"); }
}

void VerifyProjectionAggregation()
{
    var source = CreateIndexSnapshot(
        items:
        [
            new IndexedItem("P1", "inventory", 0, "Wood", 20),
            new IndexedItem("P1", "inventory", 1, "Wood", 30),
            new IndexedItem("P1", "weapon", 0, "AssaultRifle", 1, Quality: 0, DynamicItemId: "D1")
        ],
        pals:
        [
            CreateIndexedPal("A", "P1", "SheepBall", 10, lucky: false),
            CreateIndexedPal("B", "P1", "SheepBall", 20, lucky: true)
        ]);

    var result = new SaveChangeSnapshotProjector().Project(source);
    Assert(result.Items.Count == 2, "item slots with the same stable key must aggregate");
    Assert(result.Items.Single(item => item.ItemId == "Wood").Quantity == 50, "aggregated item quantity must be 50");
    Assert(result.Items.Single(item => item.ItemId == "AssaultRifle").Important, "weapon/dynamic item must be important");
    var pal = result.Pals.Single();
    Assert(pal.Count == 2 && pal.LuckyCount == 1 && Math.Abs(pal.AverageLevel - 15) < 0.001, "Pal species aggregation is incorrect");
}

void VerifyProjectionDeterminism()
{
    var source = CreateIndexSnapshot(
        players:
        [
            new IndexedPlayer("B", "", "Beta", 2, null, null, null, null, null, null, null),
            new IndexedPlayer("A", "", "Alpha", 1, null, null, null, null, null, null, null)
        ]);
    var projector = new SaveChangeSnapshotProjector();
    var first = projector.Project(source);
    var second = projector.Project(source with { Players = source.Players.Reverse().ToArray() });
    Assert(first.Players.Select(player => player.PlayerUid).SequenceEqual(["A", "B"]), "players must be sorted by stable key");
    Assert(first.Players.SequenceEqual(second.Players), "reordered source must produce identical projected players");
}

async Task VerifyComparisonAsync()
{
    using var scope = new TemporaryDirectory();
    var repository = new JsonSaveChangeSnapshotRepository(scope.Path);
    var before = CreateChangeSnapshot(
        "S1", 1,
        players:
        [
            new SaveChangePlayer("P1", "", "Alpha", 10, 1000, "G1", 0, 0, 0),
            new SaveChangePlayer("P2", "", "Removed", 5, 100, "G1", 0, 0, 0)
        ],
        guilds: [new SaveChangeGuild("G1", "Guild", 2, "P1", ["P1", "P2"])],
        bases: [new SaveChangeBase("B1", "G1", 0, 0, 0, 2, 10, "direct")],
        items: [new SaveChangeItem("P1", "weapon", "Rifle", 0, "", 20, true)],
        pals: [new SaveChangePal("P1", "SheepBall", 10, 1, 0, 12)]);
    var after = CreateChangeSnapshot(
        "S2", 2,
        players:
        [
            new SaveChangePlayer("P1", "", "Alpha", 12, 1600, "G2", 500, 0, 0),
            new SaveChangePlayer("P3", "", "Added", 1, 0, "", 0, 0, 0)
        ],
        guilds: [new SaveChangeGuild("G1", "Guild Prime", 3, "P3", ["P1", "P3"])],
        bases: [new SaveChangeBase("B1", "G2", 500, 0, 0, 3, 15, "direct")],
        items: [new SaveChangeItem("P1", "weapon", "Rifle", 0, "", 5, true)],
        pals: [new SaveChangePal("P1", "SheepBall", 4, 0, 0, 14)]);
    await repository.PublishAsync(before);
    await repository.PublishAsync(after);

    var report = await new SaveDiffService(repository, new FakeNameLookup(), TimeProvider.System).CompareAsync("S1", "S2");
    Assert(report.Summary.AddedPlayers == 1 && report.Summary.RemovedPlayers == 1 && report.Summary.ChangedPlayers == 1, "player summary is incorrect");
    Assert(report.PlayerChanges.Items.Single(change => change.PlayerUid == "P1").LevelDelta == 2, "level delta is missing");
    Assert(report.GuildChanges.Items.Single().AddedMemberPlayerUids.SequenceEqual(["P3"]), "guild member addition is missing");
    Assert(report.GuildChanges.Items.Single().RemovedMemberPlayerUids.SequenceEqual(["P2"]), "guild member removal is missing");
    Assert(report.BaseChanges.Items.Single().DistanceMoved is >= 500, "base movement threshold is not applied");
    Assert(report.ItemChanges.Items.Single().QuantityDelta == -15 && report.ItemChanges.Items.Single().Important, "important item delta is incorrect");
    Assert(report.PalChanges.Items.Single().CountDelta == -6, "Pal count delta is incorrect");
}

async Task VerifyAnomaliesAsync()
{
    using var scope = new TemporaryDirectory();
    var repository = new JsonSaveChangeSnapshotRepository(scope.Path);
    var beforePlayers = Enumerable.Range(1, 10)
        .Select(index => new SaveChangePlayer($"P{index}", "", index == 1 ? "=Formula" : $"Player {index}", 20, 0, "G1", 0, 0, 0))
        .ToArray();
    var before = CreateChangeSnapshot(
        "S1", 1,
        players: beforePlayers,
        guilds: [new SaveChangeGuild("G1", "Guild", 1, "P1", beforePlayers.Select(player => player.PlayerUid).ToArray())],
        bases: Enumerable.Range(1, 5).Select(index => new SaveChangeBase($"B{index}", "G1", index, 0, 0, 1, 1, "direct")).ToArray(),
        items: [new SaveChangeItem("P1", "keyItem", "Key", 0, "", 100, true)],
        pals: [new SaveChangePal("P1", "SheepBall", 10, 0, 0, 10)]);
    var after = CreateChangeSnapshot(
        "S2", 2,
        players: beforePlayers.Take(4).Select(player => player.PlayerUid == "P1" ? player with { Level = 10 } : player).ToArray(),
        guilds: [new SaveChangeGuild("G1", "Guild", 1, "P1", ["P1", "P2", "P3", "P4"])],
        bases: [new SaveChangeBase("B1", "G1", 1, 0, 0, 1, 1, "direct")],
        items: [new SaveChangeItem("P1", "keyItem", "Key", 0, "", 1, true)],
        pals: [new SaveChangePal("P1", "SheepBall", 2, 0, 0, 10)]);
    await repository.PublishAsync(before);
    await repository.PublishAsync(after);

    var report = await new SaveDiffService(repository, new FakeNameLookup(), TimeProvider.System).CompareLatestAsync();
    foreach (var rule in new[] { "PlayerCountDrop", "PlayerLevelDecrease", "GuildMemberDrop", "BaseCountDrop", "ImportantItemDrop", "PalCountDrop" })
        Assert(report.Anomalies.Any(anomaly => anomaly.Rule == rule), $"missing anomaly rule {rule}");
    Assert(report.Anomalies.SequenceEqual(report.Anomalies.OrderBy(anomaly => anomaly.Severity == SaveDiffSeverity.Critical ? 0 : anomaly.Severity == SaveDiffSeverity.Warning ? 1 : 2)
        .ThenBy(anomaly => anomaly.Category, StringComparer.OrdinalIgnoreCase)
        .ThenBy(anomaly => anomaly.EntityId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(anomaly => anomaly.Rule, StringComparer.OrdinalIgnoreCase)), "anomaly ordering must be deterministic");
}

async Task VerifyRepositoryAsync()
{
    using var scope = new TemporaryDirectory();
    var repository = new JsonSaveChangeSnapshotRepository(scope.Path);
    for (var index = 0; index < 31; index++)
        await repository.PublishAsync(CreateChangeSnapshot($"S{index:00}", index));
    var list = await repository.ListAsync();
    Assert(list.Count == 30, "repository must retain exactly thirty snapshots");
    Assert(list[0].SnapshotId == "S30" && list[^1].SnapshotId == "S01", "repository retention order is incorrect");
    Assert(await repository.GetAsync("S00") is null, "oldest snapshot must be deleted");

    var target = Directory.EnumerateFiles(System.IO.Path.Combine(scope.Path, "snapshots"), "*.json").First();
    await File.WriteAllTextAsync(target, "{broken");
    var targetId = list.First(entry => entry.SnapshotId == "S30").SnapshotId;
    // Locate the damaged snapshot by checking until one repository read returns null.
    var missing = new List<string>();
    foreach (var entry in list)
        if (await repository.GetAsync(entry.SnapshotId) is null) missing.Add(entry.SnapshotId);
    Assert(missing.Count == 1, "one corrupt snapshot must be removed from the catalog");
    Assert(Directory.EnumerateFiles(System.IO.Path.Combine(scope.Path, "snapshots"), "*.corrupt-*").Any(), "corrupt snapshot must be quarantined");
}

async Task VerifyRepositorySchemaAsync()
{
    using var scope = new TemporaryDirectory();
    var repository = new JsonSaveChangeSnapshotRepository(scope.Path);
    var incompatible = CreateChangeSnapshot("S1", 1) with { SchemaVersion = 2 };
    await AssertThrowsAsync<InvalidDataException>(
        () => repository.PublishAsync(incompatible),
        "future compact snapshot schemas must not be silently rewritten");
}

async Task VerifyBackfillIsolationAsync()
{
    var snapshots = new[]
    {
        CreateIndexSnapshot() with { SnapshotId = "GOOD-1", ParsedAt = DateTimeOffset.Parse("2026-07-20T00:01:00Z") },
        CreateIndexSnapshot() with { SnapshotId = "BAD", ParsedAt = DateTimeOffset.Parse("2026-07-20T00:02:00Z") },
        CreateIndexSnapshot() with { SnapshotId = "GOOD-2", ParsedAt = DateTimeOffset.Parse("2026-07-20T00:03:00Z") }
    };
    var changeRepository = new RecordingChangeRepository();
    var service = new SaveDiffBackfillService(
        new FakeIndexRepository(snapshots),
        new FailingProjector("BAD"),
        changeRepository,
        NullLogger<SaveDiffBackfillService>.Instance);

    await service.StartAsync(CancellationToken.None);
    await service.StopAsync(CancellationToken.None);

    Assert(changeRepository.Published.SequenceEqual(["GOOD-1", "GOOD-2"]),
        "one failed historical snapshot must not block later snapshots");
}

async Task VerifyInvalidPairsAsync()
{
    using var scope = new TemporaryDirectory();
    var repository = new JsonSaveChangeSnapshotRepository(scope.Path);
    var first = CreateChangeSnapshot("S1", 1);
    await repository.PublishAsync(first);
    await repository.PublishAsync(CreateChangeSnapshot("S2", 2, worldId: "W2"));
    await repository.PublishAsync(CreateChangeSnapshot("S3", 3) with { ParsedAt = first.ParsedAt });
    var service = new SaveDiffService(repository, new FakeNameLookup(), TimeProvider.System);
    await AssertThrowsAsync<SaveDiffValidationException>(() => service.CompareAsync("S1", "S1"), "same snapshots must be rejected");
    await AssertThrowsAsync<SaveDiffValidationException>(() => service.CompareAsync("S1", "S2"), "different worlds must be rejected");
    await AssertThrowsAsync<SaveDiffValidationException>(() => service.CompareAsync("S2", "S1"), "reverse order must be rejected");
    await AssertThrowsAsync<SaveDiffValidationException>(() => service.CompareAsync("S1", "S3"), "equal-time snapshots must be rejected because ordering is ambiguous");
}

void VerifyCsvSafety()
{
    Assert(SaveDiffReportWriter.NeutralizeFormula("=SUM(A1:A2)") == "'=SUM(A1:A2)", "formula prefix must be neutralized");
    Assert(SaveDiffReportWriter.EscapeCsv("a,b") == "\"a,b\"", "CSV comma escaping is incorrect");
}

static SaveIndexSnapshot CreateIndexSnapshot(
    IReadOnlyList<IndexedPlayer>? players = null,
    IReadOnlyList<IndexedItem>? items = null,
    IReadOnlyList<IndexedPal>? pals = null,
    IReadOnlyList<IndexedGuild>? guilds = null,
    IReadOnlyList<IndexedBaseCamp>? bases = null)
    => new(
        "S1", "W1", DateTimeOffset.Parse("2026-07-20T00:00:00Z"), DateTimeOffset.Parse("2026-07-20T00:01:00Z"),
        "HASH", "C:/Save", players ?? [], items ?? [], pals ?? [], guilds ?? [], bases ?? [], [],
        new Dictionary<string, string>());

static SaveChangeSnapshot CreateChangeSnapshot(
    string snapshotId,
    int minute,
    string worldId = "W1",
    IReadOnlyList<SaveChangePlayer>? players = null,
    IReadOnlyList<SaveChangeGuild>? guilds = null,
    IReadOnlyList<SaveChangeBase>? bases = null,
    IReadOnlyList<SaveChangeItem>? items = null,
    IReadOnlyList<SaveChangePal>? pals = null)
    => new(
        1,
        snapshotId,
        worldId,
        DateTimeOffset.Parse("2026-07-20T00:00:00Z").AddMinutes(minute),
        DateTimeOffset.Parse("2026-07-20T00:00:30Z").AddMinutes(minute),
        "HASH-" + snapshotId,
        players ?? [], guilds ?? [], bases ?? [], items ?? [], pals ?? []);

static IndexedPal CreateIndexedPal(string instanceId, string playerUid, string palId, int level, bool lucky)
    => new(instanceId, playerUid, palId, string.Empty, level, 0, "Unknown", lucky, false, false,
        "palbox", 0, 100, 100, 0, 0, 0, 0, 0, [], []);

static async Task AssertThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeNameLookup : IGameNameLookup
{
    public string Resolve(string kind, string id) => $"{kind}:{id}";
    public string ResolveSkillOrPassive(string id, bool includeId = true) => id;
}

sealed class FakeIndexRepository : ISaveIndexRepository
{
    private readonly IReadOnlyList<SaveIndexSnapshot> _snapshots;

    public FakeIndexRepository(IReadOnlyList<SaveIndexSnapshot> snapshots)
    {
        _snapshots = snapshots;
    }

    public Task<SaveIndexSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<SaveIndexSnapshot?>(_snapshots.OrderByDescending(item => item.ParsedAt).FirstOrDefault());
    public Task PublishAsync(SaveIndexSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordFailureAsync(SaveIndexFailure failure, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SaveIndexRepositoryHistory> GetHistoryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SaveIndexRepositoryHistory([], []));
    public Task<IReadOnlyList<SaveIndexSnapshot>> GetRecentSnapshotsAsync(int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SaveIndexSnapshot>>(_snapshots.Take(limit).ToArray());
}

sealed class FailingProjector : ISaveChangeSnapshotProjector
{
    private readonly string _failingSnapshotId;
    private readonly SaveChangeSnapshotProjector _inner = new();

    public FailingProjector(string failingSnapshotId)
    {
        _failingSnapshotId = failingSnapshotId;
    }

    public SaveChangeSnapshot Project(SaveIndexSnapshot source)
        => source.SnapshotId == _failingSnapshotId
            ? throw new InvalidDataException("synthetic damaged snapshot")
            : _inner.Project(source);
}

sealed class RecordingChangeRepository : ISaveChangeSnapshotRepository
{
    public List<string> Published { get; } = [];
    public Task PublishAsync(SaveChangeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Published.Add(snapshot.SnapshotId);
        return Task.CompletedTask;
    }
    public Task<bool> ExistsAsync(string snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<SaveChangeSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default) => Task.FromResult<SaveChangeSnapshot?>(null);
    public Task<IReadOnlyList<SaveDiffSnapshotSummary>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SaveDiffSnapshotSummary>>([]);
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "palops-save-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { }
    }
}
