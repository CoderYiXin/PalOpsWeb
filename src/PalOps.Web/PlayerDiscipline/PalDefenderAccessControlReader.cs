using System.Net;
using System.Text.Json;
using PalOps.Web.PalDefender.Configuration;

namespace PalOps.Web.PlayerDiscipline;

public interface IPalDefenderAccessControlReader
{
    Task<PalDefenderAccessControlSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class PalDefenderAccessControlReader(
    IPalDefenderConfigurationService configurationService,
    ILogger<PalDefenderAccessControlReader> logger) : IPalDefenderAccessControlReader
{
    public async Task<PalDefenderAccessControlSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var whitelist = await ReadFileAsync("WhiteList.json", warnings, cancellationToken);
        var banlist = await ReadFileAsync("Banlist.json", warnings, cancellationToken);
        var config = await ReadFileAsync("Config.json", warnings, cancellationToken);
        if (config.Content is not null && TryReadWhitelistEnabled(config.Content, out var whitelistEnabled) && !whitelistEnabled)
            warnings.Add("PalDefender Config.json 的 useWhitelist 当前为 false，名单会被保存，但不会限制未授权玩家加入。");

        var whitelistIdentifiers = whitelist.Content is null
            ? Array.Empty<string>()
            : ExtractWhitelist(whitelist.Content, warnings);
        var banRecords = banlist.Content is null
            ? Array.Empty<PalDefenderBanRecord>()
            : ExtractBans(banlist.Content, warnings);

        return new PalDefenderAccessControlSnapshot(
            whitelistIdentifiers,
            banRecords,
            whitelist.Sha256,
            banlist.Sha256,
            warnings);
    }

    private async Task<(string? Content, string Sha256)> ReadFileAsync(
        string relativePath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await configurationService.ReadAsync(relativePath, cancellationToken);
            return (file.Content, file.File.Sha256);
        }
        catch (FileNotFoundException)
        {
            warnings.Add($"{relativePath} 不存在，当前按空列表显示。请先启动一次 PalDefender 或在防护组件中创建文件。");
            return (null, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Unable to read PalDefender access-control file {Path}.", relativePath);
            warnings.Add($"无法读取 {relativePath}：{ex.Message}");
            return (null, string.Empty);
        }
    }

    private static bool TryReadWhitelistEnabled(string content, out bool enabled)
    {
        enabled = false;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("useWhitelist", StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    enabled = property.Value.GetBoolean();
                    return true;
                }
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        return false;
    }

    private static IReadOnlyList<string> ExtractWhitelist(string content, List<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectWhitelistIdentifiers(document.RootElement, results, arrayItem: false);
            return results.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException ex)
        {
            warnings.Add("WhiteList.json JSON 无效：" + ex.Message);
            return [];
        }
    }

    private static IReadOnlyList<PalDefenderBanRecord> ExtractBans(string content, List<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var results = new Dictionary<string, PalDefenderBanRecord>(StringComparer.OrdinalIgnoreCase);
            CollectBanRecords(document.RootElement, results);
            return results.Values.OrderBy(static value => value.Identifier, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException ex)
        {
            warnings.Add("Banlist.json JSON 无效：" + ex.Message);
            return [];
        }
    }

    private static void CollectWhitelistIdentifiers(JsonElement element, HashSet<string> results, bool arrayItem)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (arrayItem) AddIdentifier(element.GetString(), results);
                return;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                    CollectWhitelistIdentifiers(child, results, arrayItem: true);
                return;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsIdentifierProperty(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        AddIdentifier(property.Value.GetString(), results);
                        continue;
                    }

                    if (LooksLikeMappedIdentifier(property.Name)) AddIdentifier(property.Name, results);
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                        CollectWhitelistIdentifiers(property.Value, results, arrayItem: false);
                }
                return;
        }
    }

    private static void CollectBanRecords(JsonElement element, Dictionary<string, PalDefenderBanRecord> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray()) CollectBanRecords(child, results);
                return;
            case JsonValueKind.String:
                AddBan(element.GetString(), null, null, results);
                return;
            case JsonValueKind.Object:
                break;
            default:
                return;
        }

        var reason = FindString(element, "reason", "message", "description");
        var occurredAt = FindDate(element, "bannedAt", "banTime", "timestamp", "createdAt", "date");
        var directIdentifier = FindString(element, "userId", "userid", "user_id", "steamId", "ip", "address", "identifier");
        AddBan(directIdentifier, reason, occurredAt, results);

        foreach (var property in element.EnumerateObject())
        {
            if (LooksLikeMappedIdentifier(property.Name))
            {
                var mappedReason = property.Value.ValueKind switch
                {
                    JsonValueKind.Object => FindString(property.Value, "reason", "message", "description"),
                    JsonValueKind.String => property.Value.GetString(),
                    _ => null
                };
                var mappedAt = property.Value.ValueKind == JsonValueKind.Object
                    ? FindDate(property.Value, "bannedAt", "banTime", "timestamp", "createdAt", "date")
                    : null;
                AddBan(property.Name, mappedReason ?? reason, mappedAt ?? occurredAt, results);
            }
            if (property.Value.ValueKind == JsonValueKind.String && IsIdentifierProperty(property.Name))
                AddBan(property.Value.GetString(), reason, occurredAt, results);
            else if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                CollectBanRecords(property.Value, results);
        }
    }

    private static void AddBan(string? value, string? reason, DateTimeOffset? occurredAt, Dictionary<string, PalDefenderBanRecord> results)
    {
        var identifier = NormalizeIdentifier(value);
        if (identifier is null) return;
        var type = IPAddress.TryParse(identifier, out _) ? "ip" : "account";
        if (!results.TryGetValue(identifier, out var existing)
            || (string.IsNullOrWhiteSpace(existing.Reason) && !string.IsNullOrWhiteSpace(reason)))
            results[identifier] = new PalDefenderBanRecord(identifier, type, NormalizeText(reason, 500), occurredAt);
    }

    private static void AddIdentifier(string? value, HashSet<string> results)
    {
        var normalized = NormalizeIdentifier(value);
        if (normalized is not null) results.Add(normalized);
    }

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128) return null;
        if (normalized.Any(char.IsWhiteSpace) || normalized.Any(char.IsControl)) return null;
        if (!normalized.All(static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':')) return null;
        return IsStructuralName(normalized) ? null : normalized;
    }

    private static bool LooksLikeMappedIdentifier(string value)
    {
        var normalized = NormalizeIdentifier(value);
        if (normalized is null) return false;
        if (IPAddress.TryParse(normalized, out _)) return true;
        return normalized.Length >= 5
            && (normalized.Any(char.IsDigit)
                || normalized.Contains('_')
                || normalized.Contains(':'));
    }

    private static bool IsStructuralName(string value) => value.Equals("Users", StringComparison.OrdinalIgnoreCase)
        || value.Equals("User", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Bans", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Banlist", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Whitelist", StringComparison.OrdinalIgnoreCase)
        || value.Equals("WhiteList", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Entries", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Records", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Data", StringComparison.OrdinalIgnoreCase)
        || value.Equals("IPs", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Players", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Reason", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Message", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Description", StringComparison.OrdinalIgnoreCase)
        || value.Equals("DisplayName", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Name", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Status", StringComparison.OrdinalIgnoreCase)
        || value.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase)
        || value.Equals("BannedAt", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Timestamp", StringComparison.OrdinalIgnoreCase);

    private static bool IsIdentifierProperty(string name) =>
        name.Equals("UserId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("UserID", StringComparison.OrdinalIgnoreCase)
        || name.Equals("SteamId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Identifier", StringComparison.OrdinalIgnoreCase)
        || name.Equals("IP", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Address", StringComparison.OrdinalIgnoreCase);

    private static string? FindString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
        }
        return null;
    }

    private static DateTimeOffset? FindDate(JsonElement element, params string[] names)
    {
        var value = FindString(element, names);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? NormalizeText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
