using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Catalog;

public static partial class CatalogNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> ExistingItemCategoryMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["货币"] = "材料",
            ["武器工具"] = "武器与工具",
            ["武器与工具"] = "武器与工具",
            ["药品强化"] = "消耗品",
            ["帕鲁蛋"] = "帕鲁召唤",
            ["帕鲁召唤"] = "帕鲁召唤",
            ["食物素材"] = "食物",
            ["食物"] = "食物",
            ["关键道具"] = "关键道具",
            ["帕鲁球"] = "帕鲁球",
            ["弹药"] = "弹药",
            ["防具"] = "防具",
            ["饰品"] = "饰品",
            ["材料"] = "材料",
            ["消耗品"] = "消耗品",
            ["技能果实"] = "技能果实",
            ["设计图"] = "设计图",
            ["首领奖励"] = "首领奖励",
            ["开发/测试"] = "开发/测试",
            ["其他"] = "其他"
        };

    private static readonly HashSet<string> AmmunitionTokens =
        new(["bullet", "ammo", "arrow", "cartridge", "shell"], StringComparer.Ordinal);

    private static readonly HashSet<string> WeaponTokens =
        new([
            "rifle", "handgun", "shotgun", "launcher", "sword", "katana", "bow", "axe",
            "pickaxe", "bat", "spear", "knife", "cleaver", "grappling", "flamethrower",
            "gatling", "hammer", "mace", "staff", "blade"
        ], StringComparer.Ordinal);

    private static readonly HashSet<string> ConsumableTokens =
        new([
            "antibiotic", "medicine", "drug", "potion", "elixir", "drink", "boost",
            "fruit", "bait", "implant", "syringe"
        ], StringComparer.Ordinal);

    private static readonly HashSet<string> AccessoryTokens =
        new(["accessory", "pendant", "necklace", "goggles", "boots", "ring", "earring"],
            StringComparer.Ordinal);

    private static readonly HashSet<string> ArmorTokens =
        new(["armor", "helmet", "clothhat", "headarmor", "shield"], StringComparer.Ordinal);

    private static readonly HashSet<string> MaterialTokens =
        new([
            "skin", "scale", "scales", "claw", "claws", "fang", "ore", "leather",
            "circuit", "bone", "wood", "stone", "sand", "flint", "fiber", "wool",
            "horn", "oil", "coal", "quartz", "sulfur", "ingot", "cement", "polymer",
            "cloth", "fluid", "parts", "cell"
        ], StringComparer.Ordinal);

    private static readonly HashSet<string> FoodTokens =
        new([
            "meat", "berries", "berry", "potato", "corn", "pumpkin", "grape", "curry",
            "sandwich", "soup", "stew", "omelette", "beer", "wine", "baked", "grilled",
            "salad", "bread", "noodles", "yakisoba", "sashimi", "cake", "milk"
        ], StringComparer.Ordinal);

    private static readonly HashSet<string> FoodMaterialTokens =
        new(["seed", "ore", "skin", "scale", "scales", "bone", "leather", "horn"],
            StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> PalCategorySuffixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["首领变体"] = "（首领）",
            ["人类首领"] = "（人类首领）",
            ["突袭召唤"] = "（突袭）",
            ["塔主变体"] = "（塔主）",
            ["狂暴化"] = "（狂暴）",
            ["任务/场景变体"] = "（任务/场景）",
            ["特殊单位"] = "（特殊单位）"
        };

    private static readonly HashSet<string> TowerBossIds =
        new([
            "GrassBoss",
            "DessertBoss",
            "ForestBoss",
            "LastBoss",
            "SakurajimaBoss",
            "SnowBoss",
            "SorajimaBoss",
            "VikingBoss",
            "VolcanoBoss"
        ], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        RepositoryPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root");
        var report = await NormalizeAsync(paths, cancellationToken);
        Console.WriteLine(report["changes"]!.ToJsonString());
        return 0;
    }

    public static async Task<JsonObject> NormalizeAsync(
        RepositoryPaths paths,
        CancellationToken cancellationToken)
    {
        var seedDirectory = Path.Combine(paths.Root, "src", "PalOps.Web", "Seed");
        var itemsPath = Path.Combine(seedDirectory, "items.json");
        var palsPath = Path.Combine(seedDirectory, "pals.json");
        var items = await CatalogJson.ReadArrayAsync(itemsPath, cancellationToken);
        var pals = await CatalogJson.ReadArrayAsync(palsPath, cancellationToken);
        var itemChanges = NormalizeItems(items);
        var palChanges = NormalizePals(pals);

        await JsonFile.WriteAtomicAsync(itemsPath, items, cancellationToken);
        await JsonFile.WriteAtomicAsync(palsPath, pals, cancellationToken);

        var report = new JsonObject
        {
            ["generatedAt"] = "2026-07-16",
            ["sourcePages"] = new JsonArray(
                "https://paldeck.cc/pals",
                "https://paldeck.cc/items",
                "https://paldeck.cc/technology",
                "https://paldeck.cc/buildings",
                "https://paldeck.cc/passives",
                "https://paldeck.cc/skills"),
            ["note"] = "IDs and local images are kept offline. Names/categories were normalized to Paldeck conventions; generated labels for unreleased/development assets are explicitly descriptive rather than claimed as official Chinese localization.",
            ["changes"] = new JsonObject
            {
                ["items"] = itemChanges,
                ["pals"] = palChanges
            },
            ["catalogs"] = new JsonArray(
                Audit(paths, items, "item"),
                Audit(paths, pals, "pal"))
        };
        await JsonFile.WriteAtomicAsync(
            Path.Combine(paths.Root, "docs", "paldeck-catalog-audit.json"),
            report,
            cancellationToken);
        return report;
    }

    private static JsonObject NormalizeItems(JsonArray entries)
    {
        var changedNames = 0;
        var changedCategories = 0;
        foreach (var entry in Objects(entries))
        {
            var oldName = CatalogJson.GetString(entry, "nameZh").Trim();
            var oldCategory = CatalogJson.GetString(entry, "category");
            var identifier = CatalogJson.GetString(entry, "id");
            string newName;
            if (CatalogRules.ItemExact.TryGetValue(identifier, out var exact))
            {
                newName = exact;
            }
            else if (!HasChinese(oldName) ||
                     string.Equals(oldName, identifier, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(oldName, CatalogJson.GetString(entry, "nameEn"), StringComparison.OrdinalIgnoreCase) ||
                     oldName.Equals("en text", StringComparison.OrdinalIgnoreCase) ||
                     oldName.Equals("en_text", StringComparison.OrdinalIgnoreCase) ||
                     oldName == "-")
            {
                if (identifier.Contains("NPC", StringComparison.Ordinal))
                    newName = TranslatedFallback(identifier.Replace("_Otomo", "", StringComparison.Ordinal), "NPC/伙伴武器");
                else if (identifier.StartsWith("Debug_", StringComparison.Ordinal))
                    newName = TranslatedFallback(identifier, "开发测试");
                else if (identifier.StartsWith("Blueprint", StringComparison.Ordinal))
                    newName = TranslatedFallback(identifier, "设计图");
                else if (identifier.Contains("Test", StringComparison.Ordinal) ||
                         identifier.Contains("TEST", StringComparison.Ordinal))
                    newName = TranslatedFallback(identifier, "开发测试");
                else
                    newName = TranslatedFallback(identifier, "内部资源");
            }
            else
            {
                newName = oldName;
            }

            var newCategory = NormalizeItemCategory(entry);
            var aliases = CatalogJson.GetStringArray(entry, "aliases").ToList();
            if (!string.IsNullOrEmpty(oldName) && !string.Equals(oldName, newName, StringComparison.Ordinal))
                aliases.Add(oldName);
            aliases = aliases
                .Where(IsMeaningfulAlias)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            entry["nameZh"] = newName;
            entry["category"] = newCategory;
            CatalogJson.SetStringArray(entry, "aliases", aliases);
            entry["source"] = "paldeck-aligned-local-2026-07";
            if (!string.Equals(newName, oldName, StringComparison.Ordinal))
                changedNames++;
            if (!string.Equals(newCategory, oldCategory, StringComparison.Ordinal))
                changedCategories++;
        }

        return new JsonObject
        {
            ["changedNames"] = changedNames,
            ["changedCategories"] = changedCategories
        };
    }

    private static JsonObject NormalizePals(JsonArray entries)
    {
        var objects = Objects(entries).ToArray();
        var byId = objects.ToDictionary(
            entry => CatalogJson.GetString(entry, "id"),
            entry => entry,
            StringComparer.OrdinalIgnoreCase);
        var changedNames = 0;
        var changedCategories = 0;

        foreach (var entry in objects)
        {
            var identifier = CatalogJson.GetString(entry, "id");
            var oldName = CatalogJson.GetString(entry, "nameZh").Trim();
            var oldCategory = CatalogJson.GetString(entry, "category");
            var baseId = PalVariantPrefixPattern().Replace(identifier, "");
            byId.TryGetValue(baseId, out var baseEntry);
            var exact = CatalogRules.PalExact.TryGetValue(identifier, out var exactName)
                ? exactName
                : CatalogRules.PalExact.GetValueOrDefault(baseId);

            string newName;
            if (!string.IsNullOrEmpty(exact))
                newName = exact;
            else if (baseEntry is not null && !ReferenceEquals(baseEntry, entry) &&
                     HasChinese(CatalogJson.GetString(baseEntry, "nameZh")))
                newName = CatalogJson.GetString(baseEntry, "nameZh");
            else if (!HasChinese(oldName) || oldName is "-" or "en_text" or "en text")
                newName = TranslatedFallback(baseId, "未正式本地化帕鲁");
            else
                newName = oldName;

            var newCategory = NormalizePalCategory(
                identifier,
                CatalogJson.GetString(entry, "nameEn"),
                oldCategory);
            if (!TowerBossIds.Contains(identifier) &&
                PalCategorySuffixes.TryGetValue(newCategory, out var suffix) &&
                !newName.EndsWith(suffix, StringComparison.Ordinal))
                newName = VariantNameSuffixPattern().Replace(newName, "").Trim() + suffix;

            var aliases = CatalogJson.GetStringArray(entry, "aliases").ToList();
            if (!string.IsNullOrEmpty(oldName) && !string.Equals(oldName, newName, StringComparison.Ordinal))
                aliases.Add(oldName);
            aliases = aliases
                .Where(IsMeaningfulAlias)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            entry["nameZh"] = newName;
            entry["category"] = newCategory;
            CatalogJson.SetStringArray(entry, "aliases", aliases);
            entry["source"] = "paldeck-aligned-local-2026-07";
            if (!string.Equals(newName, oldName, StringComparison.Ordinal))
                changedNames++;
            if (!string.Equals(newCategory, oldCategory, StringComparison.Ordinal))
                changedCategories++;
        }

        return new JsonObject
        {
            ["changedNames"] = changedNames,
            ["changedCategories"] = changedCategories
        };
    }

    private static string NormalizeItemCategory(JsonObject entry)
    {
        var identifier = CatalogJson.GetString(entry, "id");
        var identifierLower = identifier.ToLowerInvariant();
        var tokens = SplitTokens(identifier)
            .Select(token => token.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var name = (
            CatalogJson.GetString(entry, "nameZh") + " " +
            CatalogJson.GetString(entry, "nameEn")).ToLowerInvariant();

        if (CatalogRules.ItemCategoryExact.TryGetValue(identifier, out var exact))
            return exact;
        if (ContainsAny(identifierLower, "debug", "test", "_tmp", "npc", "dummy") ||
            identifierLower.EndsWith("_old", StringComparison.Ordinal))
            return "开发/测试";
        if (identifierLower.StartsWith("bossdefeatreward", StringComparison.Ordinal) ||
            identifierLower == "test_bossdefeatreward")
            return "首领奖励";
        if (identifierLower.StartsWith("blueprint", StringComparison.Ordinal))
            return "设计图";
        if (identifierLower.StartsWith("skillcard_", StringComparison.Ordinal))
            return "技能果实";
        if (identifierLower.StartsWith("skillunlock_", StringComparison.Ordinal))
            return "关键道具";
        if (StartsWithAny(identifierLower, "palegg_", "summonpal", "raidsummon"))
            return "帕鲁召唤";
        if (StartsWithAny(identifierLower, "fishingbait_", "palpassiveskillchange_"))
            return "消耗品";
        if (StartsWithAny(identifierLower, "meat_", "bakedmeat_", "grilled", "roast", "fried"))
            return "食物";

        var old = CatalogJson.GetString(entry, "category");
        var fixedCategory = ExistingItemCategoryMap.GetValueOrDefault(
            old, string.IsNullOrEmpty(old) ? "其他" : old);
        if (tokens.Overlaps(AmmunitionTokens))
            return "弹药";
        if (tokens.Overlaps(WeaponTokens))
            return "武器与工具";
        if (identifierLower.Contains("palsphere", StringComparison.Ordinal) ||
            identifierLower.StartsWith("sphere_", StringComparison.Ordinal) ||
            name.Contains("帕鲁球", StringComparison.Ordinal))
            return "帕鲁球";
        if (tokens.Overlaps(ConsumableTokens))
            return "消耗品";
        if (tokens.Overlaps(AccessoryTokens))
            return "饰品";
        if (tokens.Overlaps(ArmorTokens))
            return "防具";
        if (tokens.Overlaps(MaterialTokens))
            return "材料";
        if (tokens.Overlaps(FoodTokens))
            return "食物";
        if (fixedCategory == "食物" && tokens.Overlaps(FoodMaterialTokens))
            return "材料";
        return fixedCategory;
    }

    private static bool IsHumanPalEntry(string identifier, string nameEn)
    {
        var upper = identifier.ToUpperInvariant();
        var stripped = HumanVariantPrefixPattern().Replace(upper, "");
        if (upper.StartsWith("ARENA_", StringComparison.Ordinal))
            return true;
        if (CatalogRules.HumanIdPrefixes.Any(prefix => stripped.StartsWith(prefix, StringComparison.Ordinal)))
            return true;
        var english = nameEn.Trim().ToLowerInvariant();
        return CatalogRules.HumanEnglishFragments.Any(
            fragment => english.Contains(fragment, StringComparison.Ordinal));
    }

    private static string NormalizePalCategory(string identifier, string nameEn, string old)
    {
        var upper = identifier.ToUpperInvariant();
        if (upper.Contains("TEST", StringComparison.Ordinal) ||
            upper.Contains("QUESTMAN", StringComparison.Ordinal))
            return "开发/测试";
        if (TowerBossIds.Contains(identifier))
            return "塔主变体";
        if (IsHumanPalEntry(identifier, nameEn))
            return upper.StartsWith("BOSS_", StringComparison.Ordinal) ? "人类首领" : "人类/NPC";
        if (upper.StartsWith("RAID_", StringComparison.Ordinal))
            return "突袭召唤";
        if (upper.StartsWith("BOSS_", StringComparison.Ordinal))
            return "首领变体";
        if (upper.StartsWith("GYM_", StringComparison.Ordinal) ||
            upper.StartsWith("TOWER_", StringComparison.Ordinal) ||
            upper.Contains("TOWERBOSS", StringComparison.Ordinal))
            return "塔主变体";
        if (upper.StartsWith("PREDATOR_", StringComparison.Ordinal) || old == "狂暴化")
            return "狂暴化";
        if (old == "召唤变体")
            return "召唤变体";
        if (upper.StartsWith("QUEST_", StringComparison.Ordinal) ||
            ContainsAny(upper, "_OILRIG", "_MINIOILRIG", "_LARGEOILRIG", "_ENEMYGROUP", "_QUEST_") ||
            EndsWithAny(upper, "_QUEST", "_TOWER", "_ENEMY"))
            return "任务/场景变体";
        if (upper.StartsWith("SECURITYDRONE", StringComparison.Ordinal))
            return "特殊单位";
        return "普通帕鲁";
    }

    private static string TranslatedFallback(string identifier, string prefix)
    {
        var translated = new List<string>();
        var unknown = new List<string>();
        foreach (var token in SplitTokens(identifier))
        {
            if (token.All(char.IsDigit))
            {
                translated.Add(token);
                continue;
            }
            if (CatalogRules.TokenZh.TryGetValue(token, out var mapped))
                translated.Add(mapped);
            else if (token.Length <= 3 && token == token.ToUpperInvariant())
                translated.Add(token);
            else
                unknown.Add(token);
        }

        var body = string.Join("·", translated);
        if (unknown.Count > 0)
        {
            var suffix = string.Join("_", unknown);
            body = string.IsNullOrEmpty(body) ? suffix : body + "·" + suffix;
        }
        return $"{prefix}：{(string.IsNullOrEmpty(body) ? identifier : body)}";
    }

    private static JsonObject Audit(RepositoryPaths paths, JsonArray entries, string kind)
    {
        var objects = Objects(entries).ToArray();
        var ids = objects.Select(entry => CatalogJson.GetString(entry, "id").ToLowerInvariant()).ToArray();
        var duplicateIds = ids.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingImages = objects
            .Where(entry => string.IsNullOrWhiteSpace(CatalogJson.GetString(entry, "imageUrl")))
            .Select(entry => CatalogJson.GetString(entry, "id"))
            .ToArray();
        var placeholderImages = objects
            .Where(entry => CatalogJson.GetString(entry, "imageUrl").Contains("_placeholder", StringComparison.Ordinal))
            .Select(entry => CatalogJson.GetString(entry, "id"))
            .ToArray();
        var untranslated = objects
            .Where(entry => !HasChinese(CatalogJson.GetString(entry, "nameZh")))
            .Select(entry => CatalogJson.GetString(entry, "id"))
            .ToArray();
        var staticFolder = Path.Combine(
            paths.Root, "src", "PalOps.Web", "StaticCatalog", kind == "item" ? "items" : "pals");
        var missingLocalImages = objects
            .Where(entry =>
            {
                var imageUrl = CatalogJson.GetString(entry, "imageUrl").Trim();
                if (string.IsNullOrEmpty(imageUrl))
                    return false;
                var fileName = imageUrl[(imageUrl.LastIndexOf('/') + 1)..];
                return !string.IsNullOrEmpty(fileName) && !File.Exists(Path.Combine(staticFolder, fileName));
            })
            .Select(entry => CatalogJson.GetString(entry, "id"))
            .ToArray();
        var suspiciousNames = objects
            .Where(entry =>
                LatinWordPattern().IsMatch(CatalogJson.GetString(entry, "nameZh")) &&
                CatalogJson.GetString(entry, "category") is not ("开发/测试" or "人类/NPC" or "人类首领"))
            .Select(entry => CatalogJson.GetString(entry, "id"))
            .ToArray();
        var humanMismatches = kind == "pal"
            ? objects
                .Where(entry =>
                    IsHumanPalEntry(
                        CatalogJson.GetString(entry, "id"),
                        CatalogJson.GetString(entry, "nameEn")) &&
                    CatalogJson.GetString(entry, "category") is not ("人类/NPC" or "人类首领" or "开发/测试"))
                .Select(entry => CatalogJson.GetString(entry, "id"))
                .ToArray()
            : [];
        var categories = new JsonObject();
        foreach (var group in objects
                     .GroupBy(entry => CatalogJson.GetString(entry, "category"), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
            categories[group.Key] = group.Count();

        return new JsonObject
        {
            ["type"] = kind,
            ["count"] = objects.Length,
            ["duplicateIds"] = ToJsonArray(duplicateIds),
            ["untranslatedChineseNames"] = ToJsonArray(untranslated),
            ["suspiciousChineseNames"] = ToJsonArray(suspiciousNames),
            ["humanCategoryMismatches"] = ToJsonArray(humanMismatches),
            ["missingImageUrls"] = ToJsonArray(missingImages),
            ["placeholderImageEntries"] = ToJsonArray(placeholderImages),
            ["missingLocalImages"] = ToJsonArray(missingLocalImages),
            ["categories"] = categories
        };
    }

    private static IEnumerable<JsonObject> Objects(JsonArray entries)
    {
        foreach (var node in entries)
            yield return node?.AsObject()
                         ?? throw ToolExitException.Verification("目录数组包含非对象条目。");
    }

    private static string[] SplitTokens(string value)
    {
        value = CamelCaseBoundaryPattern().Replace(value, "$1_$2");
        return NonAlphaNumericPattern().Split(value)
            .Where(part => !string.IsNullOrEmpty(part))
            .ToArray();
    }

    private static bool IsMeaningfulAlias(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
            return false;
        var lowered = normalized.ToLowerInvariant();
        if (lowered is "-" or "en text" or "en_text" or "none" or "null")
            return false;
        if (lowered.Contains("en_text", StringComparison.Ordinal) ||
            lowered.Contains("en text", StringComparison.Ordinal))
            return false;
        return MeaningfulAliasPattern().IsMatch(normalized);
    }

    private static bool HasChinese(string value) => ChinesePattern().IsMatch(value ?? string.Empty);
    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    private static bool StartsWithAny(string value, params string[] markers) =>
        markers.Any(marker => value.StartsWith(marker, StringComparison.Ordinal));
    private static bool EndsWithAny(string value, params string[] markers) =>
        markers.Any(marker => value.EndsWith(marker, StringComparison.Ordinal));

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(value);
        return result;
    }

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChinesePattern();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelCaseBoundaryPattern();

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonAlphaNumericPattern();

    [GeneratedRegex(@"[A-Za-z0-9\u4e00-\u9fff]")]
    private static partial Regex MeaningfulAliasPattern();

    [GeneratedRegex(@"^(BOSS_|RAID_|PREDATOR_|GYM_|TOWER_)", RegexOptions.IgnoreCase)]
    private static partial Regex PalVariantPrefixPattern();

    [GeneratedRegex(@"^(BOSS_|QUEST_)")]
    private static partial Regex HumanVariantPrefixPattern();

    [GeneratedRegex(@"\((BOSS|Raid)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantNameSuffixPattern();

    [GeneratedRegex(@"[A-Za-z]{4,}")]
    private static partial Regex LatinWordPattern();
}
