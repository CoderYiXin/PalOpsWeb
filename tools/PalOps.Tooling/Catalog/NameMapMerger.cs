using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Catalog;

public static partial class NameMapMerger
{
    private static readonly IReadOnlyDictionary<string, string> SourceFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["items"] = "items_id_to_name.json",
            ["pals"] = "pals_id_to_name.json",
            ["technology"] = "technology_id_to_name.json",
            ["structures"] = "structures_id_to_name.json",
            ["passives"] = "passives_id_to_name.json",
            ["skills"] = "skills_id_to_name.json"
        };

    private static readonly string[] PlaceholderMarkers =
    [
        "zh-hans text", "zh_hans_text", "zh-hans_text", "zh hans text",
        "unknown item", "unknown (", "unknown（", "en text", "en_text", "<charactername"
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> KnownBadIds =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["items"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "SkillUnlock_Anubis",
                "SkillUnlock_BlackFurDragon",
                "SkillUnlock_CaptainPenguin",
                "SkillUnlock_DarkMutant",
                "SkillUnlock_HerculesBeetle",
                "SkillUnlock_IceFox",
                "SkillUnlock_LilyQueen",
                "SkillUnlock_WingGolem"
            },
            ["pals"] = new HashSet<string>(StringComparer.Ordinal),
            ["technology"] = new HashSet<string>(StringComparer.Ordinal),
            ["structures"] = new HashSet<string>(StringComparer.Ordinal),
            ["passives"] = new HashSet<string>(StringComparer.Ordinal),
            ["skills"] = new HashSet<string>(StringComparer.Ordinal)
        };

    private static readonly IReadOnlyDictionary<string, string> CuratedItemNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BossDefeatReward_FlowerPrince"] = "夜蔓爵的花瓣",
            ["BossDefeatReward_Mothman"] = "暮尘蛾的羽毛",
            ["Blueprint_Head013_5"] = "炸蛋鸟帽设计图5"
        };

    private static readonly IReadOnlyDictionary<string, ItemAddition> CuratedItemAdditions =
        new Dictionary<string, ItemAddition>(StringComparer.Ordinal)
        {
            ["GrapplingGun_1"] = new("GrapplingGun", "爪钩枪"),
            ["GrapplingGun_2"] = new("GrapplingGun2", "高级爪钩枪"),
            ["GrapplingGun_3"] = new("GrapplingGun3", "优质爪钩枪"),
            ["GrapplingGun_4"] = new("GrapplingGun4", "特级爪钩枪"),
            ["GrapplingGun_5"] = new("GrapplingGun5", "超级爪钩枪"),
            ["PalDopingShot_3"] = new("PalDopingShot_2", "强化枪 +2"),
            ["PalEgg"] = new("PalEgg_Normal_01", "帕鲁蛋"),
            ["Premium_Processed_Wood"] = new(
                "HighGrade_Processed_Wood", "神秘木板", "/catalog/items/_placeholder.svg")
        };

    private static readonly IReadOnlyDictionary<string, string> CuratedPalNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GrassBoss"] = "佐伊 & 暴电熊"
        };

    private static readonly IReadOnlyDictionary<string, PalAddition> CuratedPalAdditions =
        new Dictionary<string, PalAddition>(StringComparer.Ordinal)
        {
            ["DessertBoss"] = new("马库斯 & 荷鲁斯", "塔主变体"),
            ["ForestBoss"] = new("莉莉 & 百合女王", "塔主变体"),
            ["LastBoss"] = new("泽娜拉 & 枯星龙", "塔主变体"),
            ["POLICE_PalRide"] = new("自卫队骑士", "人类/NPC"),
            ["SakurajimaBoss"] = new("纱夜 & 辉月伊", "塔主变体"),
            ["SnowBoss"] = new("维克托 & 异构格里芬", "塔主变体"),
            ["SorajimaBoss"] = new("苍璃 & 霄龙", "塔主变体"),
            ["VikingBoss"] = new("比约恩 & 霜牙王", "塔主变体"),
            ["VolcanoBoss"] = new("阿克塞尔 & 波鲁杰克斯", "塔主变体"),
            ["Windchimes"] = new("吊缚灵", "普通帕鲁"),
            ["Windchimes_Ice"] = new("冰缚灵", "普通帕鲁")
        };

    public static async Task<int> RunAsync(
        RepositoryPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root");
        var audit = await MergeAsync(paths, cancellationToken);
        var itemReport = audit["items"]!.AsObject();
        var palReport = audit["pals"]!.AsObject();
        Console.WriteLine(
            "ID/name merge complete: " +
            $"items replaced={itemReport["replacedCount"]} added={itemReport["addedCount"]}; " +
            $"pals replaced={palReport["replacedCount"]} added={palReport["addedCount"]}.");
        return 0;
    }

    public static async Task<JsonObject> MergeAsync(
        RepositoryPaths paths,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.Combine(paths.Root, "tools", "id-name-lists", "source");
        var seedDirectory = Path.Combine(paths.Root, "src", "PalOps.Web", "Seed");
        var nameMapDirectory = Path.Combine(seedDirectory, "NameMaps");
        var auditPath = Path.Combine(paths.Root, "docs", "id-name-list-merge-audit.json");

        var sources = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (kind, fileName) in SourceFiles)
        {
            var source = await CatalogJson.ReadObjectAsync(
                Path.Combine(sourceDirectory, fileName), cancellationToken);
            sources[kind] = CatalogJson.ToStringMap(source);
        }

        var itemsPath = Path.Combine(seedDirectory, "items.json");
        var palsPath = Path.Combine(seedDirectory, "pals.json");
        var items = await CatalogJson.ReadArrayAsync(itemsPath, cancellationToken);
        var pals = await CatalogJson.ReadArrayAsync(palsPath, cancellationToken);
        var (mergedItems, itemReport) = MergeItems(items, sources["items"]);
        var (mergedPals, palReport) = MergePals(pals, sources["pals"]);
        EnsureCaseInsensitiveUnique(mergedItems, "item");
        EnsureCaseInsensitiveUnique(mergedPals, "pal");

        await JsonFile.WriteAtomicAsync(itemsPath, mergedItems, cancellationToken);
        await JsonFile.WriteAtomicAsync(palsPath, mergedPals, cancellationToken);
        Directory.CreateDirectory(nameMapDirectory);

        var runtimeReports = new JsonObject();
        var runtimeMaps = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["items"] = BuildCatalogNameMap(mergedItems),
            ["pals"] = BuildCatalogNameMap(mergedPals)
        };
        foreach (var kind in new[] { "technology", "structures", "passives", "skills" })
        {
            var sanitized = SanitizeNameMap(sources[kind], kind);
            runtimeMaps[kind] = sanitized.Valid;
            runtimeReports[kind] = new JsonObject
            {
                ["sourceCount"] = sources[kind].Count,
                ["acceptedCount"] = sanitized.Valid.Count,
                ["rejectedCount"] = sanitized.Rejected.Count,
                ["rejected"] = sanitized.Rejected
            };
        }

        foreach (var (kind, runtimeMap) in runtimeMaps)
        {
            await JsonFile.WriteAtomicAsync(
                Path.Combine(nameMapDirectory, kind + ".json"),
                CatalogJson.StringMapToJson(runtimeMap),
                cancellationToken);
        }

        var runtimeMapReport = new JsonObject
        {
            ["items"] = new JsonObject
            {
                ["acceptedCount"] = runtimeMaps["items"].Count,
                ["source"] = "merged-catalog"
            },
            ["pals"] = new JsonObject
            {
                ["acceptedCount"] = runtimeMaps["pals"].Count,
                ["source"] = "merged-catalog"
            }
        };
        foreach (var (kind, report) in runtimeReports)
            runtimeMapReport[kind] = report?.DeepClone();

        var audit = new JsonObject
        {
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["policy"] = new JsonObject
            {
                ["runtimeOffline"] = true,
                ["invalidPlaceholdersRejected"] = true,
                ["existingCategoriesPreserved"] = true,
                ["existingImagesPreserved"] = true,
                ["omittedInternalVariantsPreserved"] = true
            },
            ["items"] = itemReport,
            ["pals"] = palReport,
            ["runtimeMaps"] = runtimeMapReport
        };
        await JsonFile.WriteAtomicAsync(auditPath, audit, cancellationToken);
        return audit;
    }

    private static (JsonArray Entries, JsonObject Report) MergeItems(
        JsonArray catalog,
        IReadOnlyDictionary<string, string> mapping)
    {
        var result = CloneArray(catalog);
        var exact = BuildIndex(result);
        var originalIds = exact.Keys.ToHashSet(StringComparer.Ordinal);
        var casefoldIds = exact.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sanitized = SanitizeNameMap(mapping, "items");
        var replacements = new JsonArray();
        var additions = new JsonArray();
        var aliasOnly = new JsonArray();
        var skippedDifferences = new JsonArray();

        foreach (var (identifier, entry) in exact.ToArray())
        {
            var oldName = CatalogJson.GetString(entry, "nameZh").Trim();
            if (CuratedItemNames.TryGetValue(identifier, out var curatedName))
            {
                if (ReplaceName(entry, curatedName))
                    replacements.Add(Change(identifier, oldName, curatedName, "curated-correction"));
                continue;
            }

            if (!sanitized.Valid.TryGetValue(identifier, out var mapped) ||
                string.Equals(mapped, oldName, StringComparison.Ordinal))
                continue;
            var quality = QualitySuffixPattern().Match(mapped);
            if (quality.Success && string.Equals(quality.Groups[1].Value, oldName, StringComparison.Ordinal))
            {
                if (ReplaceName(entry, mapped))
                    replacements.Add(Change(identifier, oldName, mapped, "quality-suffix"));
                continue;
            }

            skippedDifferences.Add(new JsonObject
            {
                ["id"] = identifier,
                ["existing"] = oldName,
                ["uploaded"] = mapped,
                ["reason"] = "existing-name-more-reliable"
            });
        }

        foreach (var (identifier, spec) in CuratedItemAdditions)
        {
            if (casefoldIds.Contains(identifier) ||
                !sanitized.Valid.TryGetValue(identifier, out var mapped) ||
                !string.Equals(mapped, spec.NameZh, StringComparison.Ordinal) ||
                !exact.TryGetValue(spec.Template, out var template))
                continue;

            var entry = CatalogJson.CloneObject(template);
            entry["id"] = identifier;
            entry["nameZh"] = spec.NameZh;
            entry["nameEn"] = identifier;
            entry["aliases"] = new JsonArray();
            entry["source"] = "user-id-name-list-2026-07-16";
            if (spec.ImageUrl is not null)
                entry["imageUrl"] = spec.ImageUrl;
            result.Add(entry);
            exact[identifier] = entry;
            casefoldIds.Add(identifier);
            additions.Add(new JsonObject
            {
                ["id"] = identifier,
                ["name"] = spec.NameZh,
                ["category"] = CatalogJson.GetString(entry, "category")
            });
        }

        return (result, BuildMergeReport(
            mapping.Count, catalog.Count, originalIds.Intersect(mapping.Keys, StringComparer.Ordinal).Count(),
            sanitized, replacements, additions, aliasOnly, skippedDifferences));
    }

    private static (JsonArray Entries, JsonObject Report) MergePals(
        JsonArray catalog,
        IReadOnlyDictionary<string, string> mapping)
    {
        var result = CloneArray(catalog);
        var exact = BuildIndex(result);
        var originalIds = exact.Keys.ToHashSet(StringComparer.Ordinal);
        var casefoldIds = exact.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sanitized = SanitizeNameMap(mapping, "pals");
        var replacements = new JsonArray();
        var additions = new JsonArray();
        var aliasOnly = new JsonArray();
        var skippedDifferences = new JsonArray();

        foreach (var (identifier, entry) in exact.ToArray())
        {
            var oldName = CatalogJson.GetString(entry, "nameZh").Trim();
            if (CuratedPalNames.TryGetValue(identifier, out var curatedName))
            {
                var oldCategory = CatalogJson.GetString(entry, "category").Trim();
                var nameChanged = ReplaceName(entry, curatedName);
                var categoryChanged = false;
                if (identifier == "GrassBoss" &&
                    !string.Equals(oldCategory, "塔主变体", StringComparison.Ordinal))
                {
                    entry["category"] = "塔主变体";
                    entry["source"] = "user-id-name-list-2026-07-16";
                    categoryChanged = true;
                }
                if (nameChanged || categoryChanged)
                {
                    replacements.Add(new JsonObject
                    {
                        ["id"] = identifier,
                        ["old"] = oldName,
                        ["new"] = curatedName,
                        ["oldCategory"] = oldCategory,
                        ["newCategory"] = CatalogJson.GetString(entry, "category"),
                        ["reason"] = "complete-tower-boss-name-and-category"
                    });
                }
                continue;
            }

            if (!sanitized.Valid.TryGetValue(identifier, out var mapped) ||
                string.Equals(mapped, oldName, StringComparison.Ordinal))
                continue;
            if (identifier.StartsWith("RAID_", StringComparison.Ordinal) &&
                (oldName.Contains("突袭", StringComparison.Ordinal) ||
                 oldName.Contains("石板", StringComparison.Ordinal)))
            {
                if (AddAlias(entry, mapped))
                {
                    aliasOnly.Add(new JsonObject
                    {
                        ["id"] = identifier,
                        ["alias"] = mapped,
                        ["reason"] = "preserve-raid-qualifier"
                    });
                }
                continue;
            }

            skippedDifferences.Add(new JsonObject
            {
                ["id"] = identifier,
                ["existing"] = oldName,
                ["uploaded"] = mapped,
                ["reason"] = "existing-name-more-descriptive"
            });
        }

        foreach (var (identifier, spec) in CuratedPalAdditions)
        {
            if (casefoldIds.Contains(identifier) ||
                !sanitized.Valid.TryGetValue(identifier, out var mapped))
                continue;
            var aliases = new JsonArray();
            if (!string.Equals(mapped, spec.Name, StringComparison.Ordinal))
                aliases.Add(mapped);
            var entry = new JsonObject
            {
                ["type"] = "pal",
                ["id"] = identifier,
                ["nameZh"] = spec.Name,
                ["nameEn"] = identifier,
                ["category"] = spec.Category,
                ["aliases"] = aliases,
                ["imageUrl"] = "/catalog/pals/_placeholder.svg",
                ["source"] = "user-id-name-list-2026-07-16"
            };
            result.Add(entry);
            exact[identifier] = entry;
            casefoldIds.Add(identifier);
            additions.Add(new JsonObject
            {
                ["id"] = identifier,
                ["name"] = spec.Name,
                ["category"] = spec.Category
            });
        }

        return (result, BuildMergeReport(
            mapping.Count, catalog.Count, originalIds.Intersect(mapping.Keys, StringComparer.Ordinal).Count(),
            sanitized, replacements, additions, aliasOnly, skippedDifferences));
    }

    private static JsonObject BuildMergeReport(
        int sourceCount,
        int existingCount,
        int commonCount,
        SanitizedNameMap sanitized,
        JsonArray replacements,
        JsonArray additions,
        JsonArray aliasOnly,
        JsonArray skippedDifferences) =>
        new()
        {
            ["sourceCount"] = sourceCount,
            ["existingCount"] = existingCount,
            ["commonCount"] = commonCount,
            ["validSourceCount"] = sanitized.Valid.Count,
            ["rejectedSourceCount"] = sanitized.Rejected.Count,
            ["replacedCount"] = replacements.Count,
            ["addedCount"] = additions.Count,
            ["aliasOnlyCount"] = aliasOnly.Count,
            ["replacements"] = replacements,
            ["additions"] = additions,
            ["aliasOnly"] = aliasOnly,
            ["skippedDifferences"] = skippedDifferences,
            ["rejected"] = sanitized.Rejected
        };

    private static SanitizedNameMap SanitizeNameMap(
        IEnumerable<KeyValuePair<string, string>> mapping,
        string kind)
    {
        var knownBad = KnownBadIds[kind];
        var valid = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new JsonArray();
        foreach (var (identifier, rawValue) in mapping.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var value = (rawValue ?? string.Empty).Trim();
            if (knownBad.Contains(identifier) || !IsValidDisplayName(value))
            {
                rejected.Add(new JsonObject
                {
                    ["id"] = identifier,
                    ["value"] = value,
                    ["reason"] = "invalid-or-unresolved"
                });
                continue;
            }
            valid[identifier] = value;
        }
        return new SanitizedNameMap(valid, rejected);
    }

    private static bool IsValidDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value == "-" ||
            value.StartsWith("- +", StringComparison.Ordinal))
            return false;
        var lowered = WhitespacePattern().Replace(value.ToLowerInvariant(), " ");
        if (PlaceholderMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
            return false;
        if (value.IndexOfAny(['<', '>', '|']) >= 0)
            return false;
        return CjkPattern().IsMatch(value);
    }

    private static bool AddAlias(JsonObject entry, string alias)
    {
        var normalized = (alias ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized) ||
            string.Equals(normalized, CatalogJson.GetString(entry, "nameZh"), StringComparison.Ordinal))
            return false;
        var aliases = CatalogJson.GetStringArray(entry, "aliases").ToList();
        if (aliases.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)))
            return false;
        aliases.Add(normalized);
        CatalogJson.SetStringArray(entry, "aliases", aliases);
        return true;
    }

    private static bool ReplaceName(
        JsonObject entry,
        string name,
        string source = "user-id-name-list-2026-07-16")
    {
        var newName = name.Trim();
        var oldName = CatalogJson.GetString(entry, "nameZh").Trim();
        if (string.IsNullOrEmpty(newName) || string.Equals(newName, oldName, StringComparison.Ordinal))
            return false;
        entry["nameZh"] = newName;
        if (!string.IsNullOrEmpty(oldName))
            AddAlias(entry, oldName);
        entry["source"] = source;
        return true;
    }

    private static void EnsureCaseInsensitiveUnique(JsonArray entries, string label)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in entries)
        {
            var entry = node?.AsObject()
                        ?? throw ToolExitException.Verification($"{label} catalog contains a non-object entry");
            var identifier = CatalogJson.GetString(entry, "id").Trim();
            if (string.IsNullOrEmpty(identifier))
                throw ToolExitException.Verification($"{label} catalog contains an empty ID");
            if (seen.TryGetValue(identifier, out var existing))
                throw ToolExitException.Verification(
                    $"{label} catalog contains case-insensitive duplicate IDs: {existing} / {identifier}");
            seen[identifier] = identifier;
        }
    }

    private static SortedDictionary<string, string> BuildCatalogNameMap(JsonArray entries)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in entries)
        {
            var entry = node!.AsObject();
            result[CatalogJson.GetString(entry, "id")] = CatalogJson.GetString(entry, "nameZh");
        }
        return result;
    }

    private static Dictionary<string, JsonObject> BuildIndex(JsonArray entries)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            var entry = node?.AsObject()
                        ?? throw ToolExitException.Verification("目录数组包含非对象条目。");
            var identifier = CatalogJson.GetString(entry, "id");
            if (!result.TryAdd(identifier, entry))
                throw ToolExitException.Verification($"目录包含重复 ID：{identifier}");
        }
        return result;
    }

    private static JsonArray CloneArray(JsonArray source)
    {
        var result = new JsonArray();
        foreach (var node in source)
            result.Add(node?.DeepClone());
        return result;
    }

    private static JsonObject Change(string id, string oldName, string newName, string reason) =>
        new()
        {
            ["id"] = id,
            ["old"] = oldName,
            ["new"] = newName,
            ["reason"] = reason
        };

    private sealed record ItemAddition(string Template, string NameZh, string? ImageUrl = null);
    private sealed record PalAddition(string Name, string Category);
    private sealed record SanitizedNameMap(SortedDictionary<string, string> Valid, JsonArray Rejected);

    [GeneratedRegex(@"[\u3400-\u9fff]")]
    private static partial Regex CjkPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^(.+?) \+([1-9]\d*)$")]
    private static partial Regex QualitySuffixPattern();
}
