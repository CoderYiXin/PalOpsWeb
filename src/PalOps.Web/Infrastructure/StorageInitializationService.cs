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

public sealed record StorageInitializationStatus(
    string State,
    bool Ready,
    bool RepairRequired,
    DateTimeOffset? AttemptedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    StorageInitializationResult? Result);

public interface IStorageInitializationService
{
    Task<StorageInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IStorageInitializationState
{
    StorageInitializationStatus Status { get; }
    Task<StorageInitializationResult> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores the latest initialization outcome. Failures are retained so the web UI can expose
/// a repair entry without preventing the application from starting.
/// </summary>
public sealed class StorageInitializationState(
    IStorageInitializationService initializer,
    ILogger<StorageInitializationState> logger) : IStorageInitializationState
{
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly object _sync = new();
    private StorageInitializationStatus _status = new("pending", false, false, null, null, null, null);

    public StorageInitializationStatus Status
    {
        get { lock (_sync) return _status; }
    }

    public async Task<StorageInitializationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var attemptedAt = DateTimeOffset.UtcNow;
            var previous = Status;
            SetStatus(new StorageInitializationStatus("running", false, false, attemptedAt, null, null, previous.Result));
            try
            {
                var result = await initializer.InitializeAsync(cancellationToken);
                SetStatus(new StorageInitializationStatus("ready", true, false, attemptedAt, DateTimeOffset.UtcNow, null, result));
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetStatus(previous);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local storage initialization failed. The repair entry will be exposed in system settings.");
                SetStatus(new StorageInitializationStatus("failed", false, true, attemptedAt, DateTimeOffset.UtcNow, ex.Message, null));
                throw;
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private void SetStatus(StorageInitializationStatus value)
    {
        lock (_sync) _status = value;
    }
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
    private sealed record StorageMetadata(int SchemaVersion, string Engine, DateTimeOffset InitializedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    public async Task<StorageInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            return await InitializeCoreAsync(cancellationToken);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<StorageInitializationResult> InitializeCoreAsync(CancellationToken cancellationToken)
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

        var metadata = await ReadMetadataAsync(paths.ResolveDataPath("storage-state.json"), items, cancellationToken);

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
            metadata.Engine,
            paths.DataDirectory,
            metadata.InitializedAt,
            readable,
            writable,
            items);
    }

    private static async Task<StorageMetadata> ReadMetadataAsync(
        string path,
        ICollection<StorageInitializationItem> items,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
            var metadata = await JsonSerializer.DeserializeAsync<StorageMetadata>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("storage-state.json 内容为空或格式无效。");
            if (metadata.SchemaVersion != 1 || !string.Equals(metadata.Engine, "atomic-json-jsonl", StringComparison.Ordinal))
                throw new InvalidDataException("storage-state.json 的存储架构版本或引擎不受支持。");
            items.Add(new StorageInitializationItem("storage-state", path, "existing", "存储元数据已存在且有效。"));
            return metadata;
        }

        var created = new StorageMetadata(1, "atomic-json-jsonl", DateTimeOffset.UtcNow);
        await AtomicWriteJsonAsync(path, created, cancellationToken);
        items.Add(new StorageInitializationItem("storage-state", path, "created", "存储元数据已创建。"));
        return created;
    }

    private static async Task EnsureJsonFileAsync<T>(
        string path,
        T defaultValue,
        ICollection<StorageInitializationItem> items,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
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
