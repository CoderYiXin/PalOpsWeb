using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.Catalog;

public interface ICatalogService
{
    Task<CatalogSearchResult> SearchAsync(string? type, string? query, string? category, bool favoritesOnly, int offset, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCategoryCount>> GetCategoriesAsync(string type, CancellationToken cancellationToken = default);
    Task<CatalogEntry?> FindAsync(string type, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CatalogEntry>> GetLookupAsync(string type, CancellationToken cancellationToken = default);
    Task<CatalogImportResult> ImportAsync(string fileName, Stream stream, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string type, string id, bool favorite, CancellationToken cancellationToken = default);
    Task SetAliasesAsync(string type, string id, IReadOnlyList<string> aliases, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(string type, IEnumerable<string> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogEntry>> ExportOverridesAsync(CancellationToken cancellationToken = default);
}

public sealed partial class CatalogService : ICatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _seedDirectory;
    private readonly string _overridesPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CatalogService(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        _seedDirectory = Path.Combine(environment.ContentRootPath, "Seed");
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _overridesPath = Path.Combine(dataDirectory, "catalog-overrides.json");
    }

    public async Task<CatalogSearchResult> SearchAsync(string? type, string? query, string? category, bool favoritesOnly, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var entries = await LoadMergedAsync(cancellationToken);
        var normalizedType = NormalizeTypeOrNull(type);
        var normalizedQuery = query?.Trim();
        var normalizedCategory = category?.Trim();

        var filtered = entries.Values.Where(entry =>
            (normalizedType is null || entry.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(normalizedCategory) || entry.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
            && (!favoritesOnly || entry.Favorite)
            && Matches(entry, normalizedQuery))
            .OrderByDescending(static entry => entry.Favorite)
            .ThenByDescending(static entry => entry.LastUsedAt)
            .ThenBy(static entry => entry.NameZh, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var safeOffset = Math.Clamp(offset, 0, 100_000);
        var safeLimit = Math.Clamp(limit, 1, 500);
        return new CatalogSearchResult(filtered.Skip(safeOffset).Take(safeLimit).ToArray(), filtered.Length);
    }

    public async Task<IReadOnlyList<CatalogCategoryCount>> GetCategoriesAsync(string type, CancellationToken cancellationToken = default)
    {
        var entries = await LoadMergedAsync(cancellationToken);
        var normalizedType = NormalizeType(type);
        return entries.Values
            .Where(entry => entry.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Category))
            .GroupBy(static entry => entry.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new CatalogCategoryCount(group.Key, group.Count()))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CatalogEntry?> FindAsync(string type, string id, CancellationToken cancellationToken = default)
    {
        var entries = await LoadMergedAsync(cancellationToken);
        entries.TryGetValue(Key(NormalizeType(type), NormalizeId(id)), out var entry);
        return entry;
    }

    public async Task<IReadOnlyDictionary<string, CatalogEntry>> GetLookupAsync(string type, CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeType(type);
        var entries = await LoadMergedAsync(cancellationToken);
        return entries.Values
            .Where(entry => entry.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CatalogImportResult> ImportAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        var importedEntries = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? await ParseCsvAsync(stream, cancellationToken)
            : await ParseJsonAsync(stream, cancellationToken);

        var valid = new List<CatalogEntry>();
        var errors = new List<string>();
        var rejected = 0;
        for (var index = 0; index < importedEntries.Count; index++)
        {
            try
            {
                valid.Add(ValidateAndNormalize(importedEntries[index] with { Source = "import" }));
            }
            catch (ArgumentException ex)
            {
                rejected++;
                errors.Add($"第 {index + 1} 条：{ex.Message}");
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var mergedBefore = await LoadMergedWithoutLockAsync(cancellationToken);
            var overrides = await LoadOverridesWithoutLockAsync(cancellationToken);
            var replaced = 0;
            var imported = 0;
            foreach (var entry in valid)
            {
                var key = Key(entry.Type, entry.Id);
                if (mergedBefore.ContainsKey(key)) replaced++; else imported++;
                overrides[key] = entry;
            }
            await SaveOverridesWithoutLockAsync(overrides.Values, cancellationToken);
            return new CatalogImportResult(imported, replaced, rejected, errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SetFavoriteAsync(string type, string id, bool favorite, CancellationToken cancellationToken = default)
        => UpdateEntryAsync(type, id, entry => entry with { Favorite = favorite, Source = entry.Source == "seed" ? "custom" : entry.Source }, cancellationToken);

    public Task SetAliasesAsync(string type, string id, IReadOnlyList<string> aliases, CancellationToken cancellationToken = default)
    {
        var normalized = aliases.Select(static alias => alias.Trim()).Where(static alias => alias.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        return UpdateEntryAsync(type, id, entry => entry with { Aliases = normalized, Source = entry.Source == "seed" ? "custom" : entry.Source }, cancellationToken);
    }

    public async Task RecordUsageAsync(string type, IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await UpdateEntryAsync(type, id, entry => entry with { LastUsedAt = timestamp, Source = entry.Source == "seed" ? "custom" : entry.Source }, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CatalogEntry>> ExportOverridesAsync(CancellationToken cancellationToken = default)
        => (await LoadOverridesAsync(cancellationToken)).Values.OrderBy(static entry => entry.Type).ThenBy(static entry => entry.Id).ToArray();

    private async Task UpdateEntryAsync(string type, string id, Func<CatalogEntry, CatalogEntry> update, CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeType(type);
        var normalizedId = NormalizeId(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var merged = await LoadMergedWithoutLockAsync(cancellationToken);
            var key = Key(normalizedType, normalizedId);
            if (!merged.TryGetValue(key, out var entry))
            {
                throw new KeyNotFoundException("目录中不存在该资源。");
            }

            var overrides = await LoadOverridesWithoutLockAsync(cancellationToken);
            overrides[key] = ValidateAndNormalize(update(entry));
            await SaveOverridesWithoutLockAsync(overrides.Values, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, CatalogEntry>> LoadMergedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadMergedWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, CatalogEntry>> LoadMergedWithoutLockAsync(CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in new[] { "items.json", "pals.json" })
        {
            var path = Path.Combine(_seedDirectory, fileName);
            if (!File.Exists(path)) continue;
            await using var stream = File.OpenRead(path);
            var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions, cancellationToken) ?? [];
            foreach (var entry in entries)
            {
                var normalized = ValidateAndNormalize(entry with
                {
                    Source = string.IsNullOrWhiteSpace(entry.Source) ? "seed" : entry.Source
                });
                merged[Key(normalized.Type, normalized.Id)] = normalized;
            }
        }

        var overrides = await LoadOverridesWithoutLockAsync(cancellationToken);
        foreach (var pair in overrides) merged[pair.Key] = pair.Value;
        return merged;
    }

    private async Task<Dictionary<string, CatalogEntry>> LoadOverridesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadOverridesWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, CatalogEntry>> LoadOverridesWithoutLockAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_overridesPath)) return result;
        await using var stream = File.OpenRead(_overridesPath);
        var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions, cancellationToken) ?? [];
        foreach (var entry in entries)
        {
            var normalized = ValidateAndNormalize(entry);
            result[Key(normalized.Type, normalized.Id)] = normalized;
        }
        return result;
    }

    private async Task SaveOverridesWithoutLockAsync(IEnumerable<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        var temporary = _overridesPath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, entries.OrderBy(static entry => entry.Type).ThenBy(static entry => entry.Id).ToArray(), JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, _overridesPath, true);
    }

    private static async Task<List<CatalogEntry>> ParseJsonAsync(Stream stream, CancellationToken cancellationToken)
        => await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions, cancellationToken)
            ?? throw new ArgumentException("JSON 必须是目录条目数组。");

    private static async Task<List<CatalogEntry>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken) ?? throw new ArgumentException("CSV 文件为空。");
        var headers = ParseCsvLine(headerLine).Select(static value => value.Trim()).ToArray();
        var result = new List<CatalogEntry>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = ParseCsvLine(line);
            var row = headers.Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                .ToDictionary(static item => item.header, static item => item.value, StringComparer.OrdinalIgnoreCase);
            row.TryGetValue("aliases", out var aliases);
            result.Add(new CatalogEntry(
                Get(row, "type"),
                Get(row, "id"),
                Get(row, "nameZh"),
                Get(row, "nameEn"),
                Get(row, "category"),
                (aliases ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Get(row, "imageUrl")));
        }
        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }
        if (quoted) throw new ArgumentException("CSV 引号未闭合。");
        values.Add(builder.ToString());
        return values;
    }

    private static CatalogEntry ValidateAndNormalize(CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var type = NormalizeType(entry.Type);
        var id = NormalizeId(entry.Id);
        var nameZh = entry.NameZh?.Trim() ?? string.Empty;
        var nameEn = entry.NameEn?.Trim() ?? string.Empty;
        var category = entry.Category?.Trim() ?? string.Empty;
        var aliases = (entry.Aliases ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        if (nameZh.Length == 0 && nameEn.Length == 0)
            throw new ArgumentException("中文名和英文名不能同时为空。");
        if (category.Length == 0)
            throw new ArgumentException("分类不能为空。");

        var imageUrl = entry.ImageUrl?.Trim() ?? string.Empty;
        if (imageUrl.Length == 0)
        {
            imageUrl = type == "item" ? "/catalog/items/_placeholder.svg" : "/catalog/pals/_placeholder.svg";
        }
        if (!imageUrl.StartsWith("/catalog/", StringComparison.OrdinalIgnoreCase)
            || imageUrl.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("图片地址必须是 /catalog/ 下的本地静态资源。");
        }

        return entry with
        {
            Type = type,
            Id = id,
            NameZh = nameZh,
            NameEn = nameEn,
            Category = category,
            Aliases = aliases,
            ImageUrl = imageUrl
        };
    }

    private static string NormalizeType(string? type)
    {
        var value = type?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "item" or "pal" ? value : throw new ArgumentException("type 只能是 item 或 pal。");
    }

    private static string? NormalizeTypeOrNull(string? type) => string.IsNullOrWhiteSpace(type) ? null : NormalizeType(type);

    private static string NormalizeId(string? id)
    {
        var value = id?.Trim() ?? string.Empty;
        if (!IdPattern().IsMatch(value)) throw new ArgumentException("资源 ID 只能包含字母、数字、下划线、连字符、冒号和点。");
        return value;
    }

    private static bool Matches(CatalogEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.NameZh.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.NameEn.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Aliases.Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string Key(string type, string id) => $"{type}:{id}";
    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var value) ? value : string.Empty;

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
