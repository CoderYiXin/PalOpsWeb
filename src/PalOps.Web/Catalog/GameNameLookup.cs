using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace PalOps.Web.Catalog;

public interface IGameNameLookup
{
    string Resolve(string kind, string id);
    string ResolveSkillOrPassive(string id, bool includeId = true);
}

public sealed class GameNameLookup : IGameNameLookup
{
    private static readonly string[] Kinds = ["items", "pals", "technology", "structures", "passives", "skills"];
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _maps;

    [ActivatorUtilitiesConstructor]
    public GameNameLookup(IHostEnvironment environment)
        : this(LoadMaps(Path.Combine(environment.ContentRootPath, "Seed", "NameMaps")))
    {
    }

    public GameNameLookup(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> maps)
    {
        _maps = maps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public string Resolve(string kind, string id)
    {
        var normalizedId = id?.Trim() ?? string.Empty;
        if (normalizedId.Length == 0) return string.Empty;
        var normalizedKind = NormalizeKind(kind);
        return _maps.TryGetValue(normalizedKind, out var map) && map.TryGetValue(normalizedId, out var name)
            ? name
            : normalizedId;
    }

    public string ResolveSkillOrPassive(string id, bool includeId = true)
    {
        var normalizedId = id?.Trim() ?? string.Empty;
        if (normalizedId.Length == 0) return string.Empty;
        _maps.TryGetValue("passives", out var passives);
        _maps.TryGetValue("skills", out var skills);
        return GameNameMapResolver.ResolveSkillOrPassive(
            normalizedId,
            passives ?? EmptyMap,
            skills ?? EmptyMap,
            includeId);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadMaps(string directory)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in Kinds)
        {
            var path = Path.Combine(directory, $"{kind}.json");
            result[kind] = LoadMap(path);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadMap(string path)
    {
        if (!File.Exists(path)) return EmptyMap;
        using var stream = File.OpenRead(path);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKind(string kind)
        => (kind?.Trim().ToLowerInvariant() ?? string.Empty) switch
        {
            "item" or "items" => "items",
            "pal" or "pals" => "pals",
            "technology" or "technologies" or "tech" => "technology",
            "structure" or "structures" or "building" or "buildings" => "structures",
            "passive" or "passives" => "passives",
            "skill" or "skills" => "skills",
            var value => value
        };

    private static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public static class GameNameMapResolver
{
    public static string ResolveSkillOrPassive(
        string id,
        IReadOnlyDictionary<string, string> passives,
        IReadOnlyDictionary<string, string> skills,
        bool includeId = true)
    {
        var normalizedId = id?.Trim() ?? string.Empty;
        if (normalizedId.Length == 0) return string.Empty;
        if (!passives.TryGetValue(normalizedId, out var name) && !skills.TryGetValue(normalizedId, out name))
        {
            return normalizedId;
        }
        return includeId ? $"{name}（{normalizedId}）" : name;
    }
}
