using System.Text.RegularExpressions;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Catalog;

public static partial class CatalogVerifier
{
    private const int ExpectedItemCount = 2463;
    private const int ExpectedPalCount = 1165;

    private static readonly IReadOnlyDictionary<string, int> ExpectedMapCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["items"] = 2463,
            ["pals"] = 1165,
            ["technology"] = 581,
            ["structures"] = 542,
            ["passives"] = 425,
            ["skills"] = 331
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedItemCategories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MeatCutterKnife"] = "武器与工具",
            ["Meat_GrassMammoth"] = "食物",
            ["BakedMeat_GrassMammoth"] = "食物",
            ["Yakisoba"] = "食物",
            ["FishingBait_1"] = "消耗品",
            ["AssaultRifleBullet"] = "弹药",
            ["SkillUnlock_LazyDragon"] = "关键道具",
            ["SkillCard_RockLance"] = "技能果实",
            ["GrapplingGun_1"] = "武器与工具",
            ["Premium_Processed_Wood"] = "材料"
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedPalCategories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SoldierBee"] = "普通帕鲁",
            ["GuardianDog"] = "普通帕鲁",
            ["Hunter_Rifle"] = "人类/NPC",
            ["Male_Trader01"] = "人类/NPC",
            ["BOSS_Hunter_Rifle"] = "人类首领",
            ["Quest_Farmer03_SheepBall"] = "任务/场景变体",
            ["WingGolem_Oilrig"] = "任务/场景变体",
            ["SecurityDrone"] = "特殊单位",
            ["RAID_NightLady"] = "突袭召唤",
            ["GYM_ElecPanda"] = "塔主变体",
            ["GrassBoss"] = "塔主变体",
            ["DessertBoss"] = "塔主变体"
        };

    private static readonly IReadOnlyDictionary<(string Kind, string Id), string> ExpectedNames =
        new Dictionary<(string Kind, string Id), string>
        {
            [("items", "HandGun_Default_4")] = "手枪 +3",
            [("items", "BossDefeatReward_FlowerPrince")] = "夜蔓爵的花瓣",
            [("items", "BossDefeatReward_Mothman")] = "暮尘蛾的羽毛",
            [("pals", "GrassBoss")] = "佐伊 & 暴电熊",
            [("pals", "RAID_YakushimaBoss002")] = "月亮领主(石板)（突袭）"
        };

    private static readonly string[] BadMarkers =
    [
        "zh-hans text",
        "zh_hans_text",
        "unknown (",
        "unknown item",
        "<charactername"
    ];

    public static async Task<int> RunAsync(
        RepositoryPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root");
        var problems = await VerifyAsync(paths, cancellationToken);
        if (problems.Count > 0)
            throw ToolExitException.Verification(
                "Catalog verification failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Take(100).Select(problem => "- " + problem)) +
                (problems.Count > 100 ? $"{Environment.NewLine}- ... 另有 {problems.Count - 100} 个问题" : string.Empty));

        Console.WriteLine(
            $"PASS catalog integrity: items={ExpectedItemCount}, pals={ExpectedPalCount}, six name maps valid, IDs unique, names/categories/images valid");
        return 0;
    }

    public static async Task<IReadOnlyList<string>> VerifyAsync(
        RepositoryPaths paths,
        CancellationToken cancellationToken)
    {
        var seed = Path.Combine(paths.Root, "src", "PalOps.Web", "Seed");
        var staticRoot = Path.Combine(paths.Root, "src", "PalOps.Web", "StaticCatalog");
        var items = await JsonFile.ReadAsync<List<CatalogEntryModel>>(
            Path.Combine(seed, "items.json"), cancellationToken);
        var pals = await JsonFile.ReadAsync<List<CatalogEntryModel>>(
            Path.Combine(seed, "pals.json"), cancellationToken);
        var problems = new List<string>();

        if (items.Count != ExpectedItemCount)
            problems.Add($"items: expected {ExpectedItemCount} entries, got {items.Count}");
        if (pals.Count != ExpectedPalCount)
            problems.Add($"pals: expected {ExpectedPalCount} entries, got {pals.Count}");

        VerifyCatalog("items", "item", items, Path.Combine(staticRoot, "items"), problems);
        VerifyCatalog("pals", "pal", pals, Path.Combine(staticRoot, "pals"), problems);
        VerifyExamples("items", items, ExpectedItemCategories, problems);
        VerifyExamples("pals", pals, ExpectedPalCategories, problems);

        var catalogs = new Dictionary<string, IReadOnlyDictionary<string, CatalogEntryModel>>(StringComparer.Ordinal)
        {
            ["items"] = items.GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
            ["pals"] = pals.GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
        };
        foreach (var (key, expectedName) in ExpectedNames)
        {
            if (!catalogs[key.Kind].TryGetValue(key.Id, out var entry))
            {
                problems.Add($"{key.Kind}: missing expected ID {key.Id}");
                continue;
            }
            if (!string.Equals(entry.NameZh, expectedName, StringComparison.Ordinal))
                problems.Add($"{key.Kind}:{key.Id}: expected name {expectedName}, got {entry.NameZh}");
        }

        foreach (var (kind, expectedCount) in ExpectedMapCounts)
        {
            var path = Path.Combine(seed, "NameMaps", kind + ".json");
            var values = await JsonFile.ReadAsync<Dictionary<string, string>>(path, cancellationToken);
            VerifyNameMap(kind, expectedCount, values, problems);
        }

        return problems;
    }

    private static void VerifyCatalog(
        string name,
        string expectedType,
        IReadOnlyList<CatalogEntryModel> entries,
        string imageFolder,
        List<string> problems)
    {
        var ids = entries.Select(entry => entry.Id.Trim()).ToArray();
        if (ids.Any(string.IsNullOrEmpty))
            problems.Add($"{name}: contains empty IDs");
        var duplicates = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Take(10)
            .ToArray();
        if (duplicates.Length > 0)
            problems.Add($"{name}: duplicate IDs: {string.Join(", ", duplicates)}");

        foreach (var entry in entries)
        {
            var identifier = entry.Id;
            if (!string.Equals(entry.Type, expectedType, StringComparison.Ordinal))
                problems.Add($"{name}:{identifier}: invalid type");
            if (!CjkPattern().IsMatch(entry.NameZh))
                problems.Add($"{name}:{identifier}: missing Chinese display name");
            var lowered = entry.NameZh.ToLowerInvariant();
            if (BadMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
                problems.Add($"{name}:{identifier}: unresolved display name");
            if (string.IsNullOrWhiteSpace(entry.Category))
                problems.Add($"{name}:{identifier}: missing category");

            var expectedPrefix = $"/catalog/{name}/";
            if (!entry.ImageUrl.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                problems.Add($"{name}:{identifier}: invalid local image URL");
                continue;
            }

            var fileName = entry.ImageUrl[(entry.ImageUrl.LastIndexOf('/') + 1)..];
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..", StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(imageFolder, fileName)))
                problems.Add($"{name}:{identifier}: missing image file");
        }
    }

    private static void VerifyExamples(
        string label,
        IReadOnlyList<CatalogEntryModel> entries,
        IReadOnlyDictionary<string, string> expected,
        List<string> problems)
    {
        var byId = entries.GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var (identifier, category) in expected)
        {
            if (!byId.TryGetValue(identifier, out var entry))
            {
                problems.Add($"{label}: missing expected ID {identifier}");
                continue;
            }
            if (!string.Equals(entry.Category, category, StringComparison.Ordinal))
                problems.Add($"{label}:{identifier}: expected category {category}, got {entry.Category}");
        }
    }

    private static void VerifyNameMap(
        string kind,
        int expectedCount,
        IReadOnlyDictionary<string, string> values,
        List<string> problems)
    {
        if (values.Count != expectedCount)
            problems.Add($"{kind}: expected {expectedCount} mapped names, got {values.Count}");
        var duplicate = values.Keys.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            problems.Add($"{kind}: duplicate case-insensitive ID {duplicate.Key}");

        foreach (var (identifier, value) in values)
        {
            var text = value.Trim();
            var lowered = text.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(identifier))
                problems.Add($"{kind}: empty ID");
            if (!CjkPattern().IsMatch(text))
                problems.Add($"{kind}:{identifier}: missing Chinese display name");
            if (BadMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
                problems.Add($"{kind}:{identifier}: unresolved display name");
            if (text.IndexOfAny(['<', '>', '|']) >= 0)
                problems.Add($"{kind}:{identifier}: unresolved markup");
        }
    }

    [GeneratedRegex(@"[\u3400-\u9fff]")]
    private static partial Regex CjkPattern();
}
