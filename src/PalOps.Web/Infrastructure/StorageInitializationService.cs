using System.Text.Json;

namespace PalOps.Web.Infrastructure;

public sealed record StorageInitializationItem(
    string Name,
    string Path,
    string Status,
    string Message);

public sealed record StorageInitializationResult(
    string Engine,
    string DataDirectory,
    DateTimeOffset InitializedAt,
    bool Readable,
    bool Writable,
    IReadOnlyList<StorageInitializationItem> Items);

public interface IStorageInitializationService
{
    Task<StorageInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Initializes the local persistence tree without deleting or overwriting existing data.
/// PalOps currently uses atomic JSON/JSONL repositories; this operation creates only
/// missing directories and empty repository files, then verifies read/write access.
/// </summary>
public sealed class StorageInitializationService(
    IRuntimePathResolver paths,
    ILogger<StorageInitializationService> logger) : IStorageInitializationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<StorageInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var items = new List<StorageInitializationItem>();
        var directories = new[]
        {
            "keys",
            "logs",
            "audit",
            "backups",
            Path.Combine("backups", "files"),
            "automation",
            "map",
            "save-index",
            Path.Combine("save-index", "snapshots"),
            "server-operation-history",
            "webhook-history",
            Path.Combine("snapshots", "incoming")
        };

        Directory.CreateDirectory(paths.DataDirectory);
        foreach (var relative in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            var fullPath = paths.ResolveDataPath(segments);
            var existed = Directory.Exists(fullPath);
            Directory.CreateDirectory(fullPath);
            items.Add(new StorageInitializationItem(
                relative.Replace(Path.DirectorySeparatorChar, '/'),
                fullPath,
                existed ? "existing" : "created",
                existed ? "目录已存在。" : "目录已创建。"));
        }

        await EnsureJsonFileAsync(paths.ResolveDataPath("backups", "index.json"), Array.Empty<object>(), items, cancellationToken);
        await EnsureJsonFileAsync(paths.ResolveDataPath("automation", "jobs.json"), Array.Empty<object>(), items, cancellationToken);
        await EnsureJsonFileAsync(paths.ResolveDataPath("map", "custom-markers.json"), Array.Empty<object>(), items, cancellationToken);
        await EnsureTextFileAsync(paths.ResolveDataPath("automation", "runs.ndjson"), items, cancellationToken);
        await EnsureTextFileAsync(paths.ResolveDataPath("save-index", "failures.ndjson"), items, cancellationToken);

        var metadataPath = paths.ResolveDataPath("storage-state.json");
        var metadata = new
        {
            schemaVersion = 1,
            engine = "atomic-json-jsonl",
            initializedAt = DateTimeOffset.UtcNow
        };
        await AtomicWriteJsonAsync(metadataPath, metadata, cancellationToken);
        items.Add(new StorageInitializationItem(
            "storage-state",
            metadataPath,
            "updated",
            "存储元数据已更新。"));

        var probePath = paths.ResolveDataPath($".write-probe-{Guid.NewGuid():N}.tmp");
        var readable = false;
        var writable = false;
        try
        {
            await File.WriteAllTextAsync(probePath, "palops-storage-probe", cancellationToken);
            writable = true;
            readable = string.Equals(
                await File.ReadAllTextAsync(probePath, cancellationToken),
                "palops-storage-probe",
                StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to remove storage write probe {ProbePath}.", probePath);
            }
        }

        if (!readable || !writable)
            throw new IOException("本地数据目录读写验证失败，请检查运行账户的目录权限。");

        return new StorageInitializationResult(
            "atomic-json-jsonl",
            paths.DataDirectory,
            DateTimeOffset.UtcNow,
            readable,
            writable,
            items);
    }

    private static async Task EnsureJsonFileAsync<T>(
        string path,
        T defaultValue,
        ICollection<StorageInitializationItem> items,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            // Validate existing JSON without rewriting user data.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
            if (stream.Length > 0) _ = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            items.Add(new StorageInitializationItem(Path.GetFileName(path), path, "existing", "数据文件已存在且 JSON 有效。"));
            return;
        }

        await AtomicWriteJsonAsync(path, defaultValue, cancellationToken);
        items.Add(new StorageInitializationItem(Path.GetFileName(path), path, "created", "空数据文件已创建。"));
    }

    private static async Task EnsureTextFileAsync(
        string path,
        ICollection<StorageInitializationItem> items,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            items.Add(new StorageInitializationItem(Path.GetFileName(path), path, "existing", "日志型数据文件已存在。"));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, string.Empty, cancellationToken);
        items.Add(new StorageInitializationItem(Path.GetFileName(path), path, "created", "空日志型数据文件已创建。"));
    }

    private static async Task AtomicWriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
