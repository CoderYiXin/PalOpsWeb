using System.Text.Json;
using System.Text.Json.Nodes;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Catalog;

internal static class CatalogJson
{
    public static async Task<JsonArray> ReadArrayAsync(string path, CancellationToken cancellationToken)
    {
        var node = await ReadNodeAsync(path, cancellationToken);
        return node as JsonArray
               ?? throw ToolExitException.Verification($"{path} 必须包含 JSON 数组。");
    }

    public static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        var node = await ReadNodeAsync(path, cancellationToken);
        return node as JsonObject
               ?? throw ToolExitException.Verification($"{path} 必须包含 JSON 对象。");
    }

    public static string GetString(JsonObject value, string propertyName) =>
        value[propertyName]?.GetValue<string>() ?? string.Empty;

    public static IReadOnlyList<string> GetStringArray(JsonObject value, string propertyName)
    {
        if (value[propertyName] is not JsonArray array)
            return [];
        return array
            .Where(item => item is not null)
            .Select(item => item!.GetValue<string>())
            .ToArray();
    }

    public static void SetStringArray(JsonObject value, string propertyName, IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(item);
        value[propertyName] = array;
    }

    public static SortedDictionary<string, string> ToStringMap(JsonObject value)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, node) in value)
            result[key] = node?.GetValue<string>() ?? string.Empty;
        return result;
    }

    public static JsonObject StringMapToJson(IEnumerable<KeyValuePair<string, string>> values)
    {
        var result = new JsonObject();
        foreach (var (key, value) in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            result[key] = value;
        return result;
    }

    public static JsonObject CloneObject(JsonObject value) =>
        value.DeepClone().AsObject();

    private static async Task<JsonNode> ReadNodeAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw ToolExitException.Verification($"缺少 JSON 文件：{path}");
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        try
        {
            return await JsonNode.ParseAsync(
                       stream,
                       new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                       new JsonDocumentOptions
                       {
                           AllowTrailingCommas = true,
                           CommentHandling = JsonCommentHandling.Skip
                       },
                       cancellationToken)
                   ?? throw ToolExitException.Verification($"JSON 文件为空：{path}");
        }
        catch (JsonException exception)
        {
            throw ToolExitException.Verification($"JSON 格式无效：{path}：{exception.Message}");
        }
    }
}
