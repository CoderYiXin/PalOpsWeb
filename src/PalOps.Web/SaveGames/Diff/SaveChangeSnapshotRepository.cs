using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalOps.Web.SaveGames.Diff;

public interface ISaveChangeSnapshotRepository
{
    Task PublishAsync(SaveChangeSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string snapshotId, CancellationToken cancellationToken = default);
    Task<SaveChangeSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaveDiffSnapshotSummary>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonSaveChangeSnapshotRepository : ISaveChangeSnapshotRepository
{
    internal const int MaxSnapshots = 30;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly string _snapshotsRoot;
    private readonly string _catalogPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<SaveDiffSnapshotSummary>? _cachedCatalog;

    public JsonSaveChangeSnapshotRepository(string root)
    {
        _root = Path.GetFullPath(root);
        _snapshotsRoot = Path.Combine(_root, "snapshots");
        _catalogPath = Path.Combine(_root, "index.json");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_snapshotsRoot);
    }

    public async Task PublishAsync(SaveChangeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotId))
            throw new ArgumentException("快照 ID 不能为空。", nameof(snapshot));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = Normalize(snapshot);
            var path = SnapshotPath(normalized.SnapshotId);
            await WriteAtomicJsonAsync(path, normalized, cancellationToken);

            var catalog = (await LoadCatalogWithoutLockAsync(cancellationToken))
                .Where(entry => !entry.SnapshotId.Equals(normalized.SnapshotId, StringComparison.OrdinalIgnoreCase))
                .Append(ToSummary(normalized))
                .OrderByDescending(entry => entry.ParsedAt)
                .ThenByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.SnapshotId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var keep = catalog.Take(MaxSnapshots).ToArray();
            var dropped = catalog.Skip(MaxSnapshots).ToArray();
            await WriteAtomicJsonAsync(_catalogPath, new CatalogDocument(1, keep), cancellationToken);
            _cachedCatalog = keep;

            foreach (var entry in dropped)
                TryDelete(SnapshotPath(entry.SnapshotId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ExistsAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotId)) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogWithoutLockAsync(cancellationToken);
            return catalog.Any(entry => entry.SnapshotId.Equals(snapshotId, StringComparison.OrdinalIgnoreCase))
                   && File.Exists(SnapshotPath(snapshotId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveChangeSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotId)) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogWithoutLockAsync(cancellationToken);
            if (!catalog.Any(entry => entry.SnapshotId.Equals(snapshotId, StringComparison.OrdinalIgnoreCase)))
                return null;

            var path = SnapshotPath(snapshotId);
            if (!File.Exists(path))
            {
                await RemoveCatalogEntryWithoutLockAsync(snapshotId, catalog, cancellationToken);
                return null;
            }

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                var snapshot = await JsonSerializer.DeserializeAsync<SaveChangeSnapshot>(stream, JsonOptions, cancellationToken);
                if (snapshot is null) throw new JsonException("快照内容为空。");
                return Normalize(snapshot);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException)
            {
                Quarantine(path);
                await RemoveCatalogEntryWithoutLockAsync(snapshotId, catalog, cancellationToken);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SaveDiffSnapshotSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogWithoutLockAsync(cancellationToken);
            var existing = catalog.Where(entry => File.Exists(SnapshotPath(entry.SnapshotId))).ToArray();
            if (existing.Length != catalog.Count)
            {
                await WriteAtomicJsonAsync(_catalogPath, new CatalogDocument(1, existing), cancellationToken);
                _cachedCatalog = existing;
            }
            return existing;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<SaveDiffSnapshotSummary>> LoadCatalogWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (_cachedCatalog is not null) return _cachedCatalog;
        if (!File.Exists(_catalogPath))
            return _cachedCatalog = await RebuildCatalogWithoutLockAsync(cancellationToken);

        try
        {
            await using var stream = new FileStream(_catalogPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            var document = await JsonSerializer.DeserializeAsync<CatalogDocument>(stream, JsonOptions, cancellationToken);
            if (document is null || document.SchemaVersion != 1)
                throw new JsonException("快照目录版本无效。");
            return _cachedCatalog = document.Snapshots
                .OrderByDescending(entry => entry.ParsedAt)
                .ThenBy(entry => entry.SnapshotId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxSnapshots)
                .ToArray();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException)
        {
            Quarantine(_catalogPath);
            return _cachedCatalog = await RebuildCatalogWithoutLockAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<SaveDiffSnapshotSummary>> RebuildCatalogWithoutLockAsync(CancellationToken cancellationToken)
    {
        var entries = new List<SaveDiffSnapshotSummary>();
        foreach (var path in Directory.EnumerateFiles(_snapshotsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                var snapshot = await JsonSerializer.DeserializeAsync<SaveChangeSnapshot>(stream, JsonOptions, cancellationToken);
                if (snapshot is not null) entries.Add(ToSummary(Normalize(snapshot)));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException)
            {
                Quarantine(path);
            }
        }

        var result = entries
            .GroupBy(entry => entry.SnapshotId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.ParsedAt).First())
            .OrderByDescending(entry => entry.ParsedAt)
            .ThenBy(entry => entry.SnapshotId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSnapshots)
            .ToArray();
        var retainedIds = result
            .Select(entry => entry.SnapshotId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => !retainedIds.Contains(entry.SnapshotId)))
            TryDelete(SnapshotPath(entry.SnapshotId));

        await WriteAtomicJsonAsync(_catalogPath, new CatalogDocument(1, result), cancellationToken);
        return result;
    }

    private async Task RemoveCatalogEntryWithoutLockAsync(
        string snapshotId,
        IReadOnlyList<SaveDiffSnapshotSummary> current,
        CancellationToken cancellationToken)
    {
        var updated = current
            .Where(entry => !entry.SnapshotId.Equals(snapshotId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await WriteAtomicJsonAsync(_catalogPath, new CatalogDocument(1, updated), cancellationToken);
        _cachedCatalog = updated;
    }

    private string SnapshotPath(string snapshotId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotId.Trim()));
        return Path.Combine(_snapshotsRoot, Convert.ToHexString(bytes).ToLowerInvariant() + ".json");
    }

    private static SaveChangeSnapshot Normalize(SaveChangeSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的差异快照版本：{snapshot.SchemaVersion}。");

        var normalized = snapshot with
        {
            SnapshotId = snapshot.SnapshotId?.Trim() ?? string.Empty,
            WorldId = snapshot.WorldId?.Trim() ?? string.Empty,
            LevelSha256 = snapshot.LevelSha256?.Trim() ?? string.Empty,
            Players = snapshot.Players ?? [],
            Guilds = snapshot.Guilds ?? [],
            Bases = snapshot.Bases ?? [],
            Items = snapshot.Items ?? [],
            Pals = snapshot.Pals ?? []
        };

        if (normalized.SnapshotId.Length == 0)
            throw new InvalidDataException("差异快照 ID 不能为空。");
        if (normalized.WorldId.Length == 0)
            throw new InvalidDataException("差异快照世界 ID 不能为空。");

        return normalized;
    }

    public static SaveDiffSnapshotSummary ToSummary(SaveChangeSnapshot snapshot)
        => new(
            snapshot.SnapshotId,
            snapshot.WorldId,
            snapshot.CreatedAt,
            snapshot.ParsedAt,
            snapshot.LevelSha256,
            snapshot.Players.Count,
            snapshot.Guilds.Count,
            snapshot.Bases.Count,
            snapshot.Items.Count,
            snapshot.Items.Sum(item => (long)item.Quantity),
            snapshot.Pals.Count,
            snapshot.Pals.Sum(pal => pal.Count));

    private static async Task WriteAtomicJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void Quarantine(string path)
    {
        if (!File.Exists(path)) return;
        var quarantined = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        try { File.Move(path, quarantined, overwrite: false); }
        catch (IOException) { TryDelete(path); }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record CatalogDocument(int SchemaVersion, IReadOnlyList<SaveDiffSnapshotSummary> Snapshots);
}
