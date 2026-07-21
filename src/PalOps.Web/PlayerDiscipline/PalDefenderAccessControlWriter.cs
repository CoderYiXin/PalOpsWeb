using System.Text.Json;
using System.Text.Json.Nodes;
using PalOps.Web.PalDefender.Configuration;

namespace PalOps.Web.PlayerDiscipline;

public sealed record PalDefenderWhitelistWriteResult(
    bool Changed,
    string Sha256,
    IReadOnlyList<string> WhitelistIdentifiers);

public interface IPalDefenderAccessControlWriter
{
    Task<PalDefenderWhitelistWriteResult> AddWhitelistAsync(string userId, CancellationToken cancellationToken = default);
    Task<PalDefenderWhitelistWriteResult> RemoveWhitelistAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class PalDefenderAccessControlWriter(
    IPalDefenderConfigurationService configurationService) : IPalDefenderAccessControlWriter
{
    private const string WhitelistPath = "WhiteList.json";
    private static readonly JsonSerializerOptions PrettyJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] CollectionPropertyNames = ["WhiteList", "Whitelist", "Users", "Entries", "Data", "Players"];

    public Task<PalDefenderWhitelistWriteResult> AddWhitelistAsync(string userId, CancellationToken cancellationToken = default)
        => SetMembershipAsync(userId, include: true, cancellationToken);

    public Task<PalDefenderWhitelistWriteResult> RemoveWhitelistAsync(string userId, CancellationToken cancellationToken = default)
        => SetMembershipAsync(userId, include: false, cancellationToken);

    private async Task<PalDefenderWhitelistWriteResult> SetMembershipAsync(
        string userId,
        bool include,
        CancellationToken cancellationToken)
    {
        PalDefenderConfigFileContent? current = null;
        try
        {
            current = await configurationService.ReadAsync(WhitelistPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            if (!include)
                return new PalDefenderWhitelistWriteResult(false, string.Empty, []);
        }

        var root = ParseRoot(current?.Content);
        var changed = Mutate(root, userId, include);
        var identifiers = ExtractIdentifiers(root);
        if (!changed)
            return new PalDefenderWhitelistWriteResult(false, current?.File.Sha256 ?? string.Empty, identifiers);

        var content = root.ToJsonString(PrettyJson) + Environment.NewLine;
        var saved = await configurationService.SaveAsync(
            WhitelistPath,
            content,
            current?.File.Sha256,
            cancellationToken);

        return new PalDefenderWhitelistWriteResult(
            true,
            saved.File.Sha256,
            ExtractIdentifiers(ParseRoot(saved.Content)));
    }

    internal static JsonNode ParseRoot(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new JsonArray();
        return JsonNode.Parse(content, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        }) ?? new JsonArray();
    }

    internal static bool Mutate(JsonNode root, string userId, bool include)
    {
        if (root is JsonArray array)
            return MutateArray(array, userId, include);

        if (root is not JsonObject obj)
            throw new InvalidDataException("WhiteList.json 根节点必须是 JSON 数组或对象。");

        foreach (var name in CollectionPropertyNames)
        {
            var property = obj.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property.Value is JsonArray collection)
                return MutateArray(collection, userId, include);
        }

        var mappedKey = obj.Select(static pair => pair.Key)
            .FirstOrDefault(key => key.Equals(userId, StringComparison.OrdinalIgnoreCase));
        if (mappedKey is not null)
        {
            if (include) return false;
            return obj.Remove(mappedKey);
        }

        var looksLikeIdentifierMap = obj.Count > 0 && obj.All(static pair => LooksLikeIdentifier(pair.Key));
        if (looksLikeIdentifierMap)
        {
            if (!include) return false;
            obj[userId] = true;
            return true;
        }

        if (!include)
            return RemoveNested(obj, userId);

        var target = new JsonArray();
        obj["WhiteList"] = target;
        target.Add(userId);
        return true;
    }

    private static bool MutateArray(JsonArray array, string userId, bool include)
    {
        var matches = array
            .Select((node, index) => (node, index))
            .Where(item => item.node is JsonValue value
                && value.TryGetValue<string>(out var text)
                && text.Equals(userId, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.index)
            .ToArray();

        if (include)
        {
            if (matches.Length > 0) return false;
            array.Add(userId);
            return true;
        }

        for (var index = matches.Length - 1; index >= 0; index--)
            array.RemoveAt(matches[index]);
        return matches.Length > 0;
    }

    private static bool RemoveNested(JsonObject obj, string userId)
    {
        var changed = false;
        foreach (var pair in obj.ToArray())
        {
            if (pair.Key.Equals(userId, StringComparison.OrdinalIgnoreCase))
            {
                changed |= obj.Remove(pair.Key);
                continue;
            }

            switch (pair.Value)
            {
                case JsonArray array:
                    changed |= MutateArray(array, userId, include: false);
                    break;
                case JsonObject child:
                    changed |= RemoveNested(child, userId);
                    break;
            }
        }
        return changed;
    }

    private static IReadOnlyList<string> ExtractIdentifiers(JsonNode root)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(root, results, arrayItem: false);
        return results.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void Collect(JsonNode? node, HashSet<string> results, bool arrayItem)
    {
        switch (node)
        {
            case JsonValue value when arrayItem && value.TryGetValue<string>(out var text):
                if (LooksLikeIdentifier(text)) results.Add(text.Trim());
                return;
            case JsonArray array:
                foreach (var child in array) Collect(child, results, arrayItem: true);
                return;
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (LooksLikeIdentifier(pair.Key)) results.Add(pair.Key.Trim());
                    if (pair.Value is JsonArray or JsonObject) Collect(pair.Value, results, arrayItem: false);
                }
                return;
        }
    }

    private static bool LooksLikeIdentifier(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length is < 5 or > 128) return false;
        if (normalized.Any(char.IsWhiteSpace) || normalized.Any(char.IsControl)) return false;
        if (!normalized.All(static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':')) return false;
        return normalized.Any(char.IsDigit) || normalized.Contains('_') || normalized.Contains(':');
    }
}
