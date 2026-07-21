using System.Text.Json;

namespace PalOps.Web.SaveGames.Index;

public interface ISaveIndexRepository
{
    Task<SaveIndexSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(SaveIndexSnapshot snapshot, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(SaveIndexFailure failure, CancellationToken cancellationToken = default);
    Task<SaveIndexRepositoryHistory> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaveIndexSnapshot>> GetRecentSnapshotsAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Versioned JSON index store. Publishing writes a complete temporary file and then
/// atomically replaces current.json, so a failed parse can never erase the previous
/// successful player index.
/// </summary>
public sealed class JsonSaveIndexRepository : ISaveIndexRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly string _snapshotsRoot;
    private readonly string _currentPath;
    private readonly string _failurePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SaveIndexSnapshot? _cachedCurrent;
    private CurrentFileFingerprint? _cachedCurrentFingerprint;

    public JsonSaveIndexRepository(string root)
    {
        _root = Path.GetFullPath(root);
        _snapshotsRoot = Path.Combine(_root, "snapshots");
        _currentPath = Path.Combine(_root, "current.json");
        _failurePath = Path.Combine(_root, "failures.ndjson");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_snapshotsRoot);
    }

    public async Task<SaveIndexSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_currentPath))
            {
                _cachedCurrent = null;
                _cachedCurrentFingerprint = null;
                return null;
            }

            var fingerprint = CurrentFileFingerprint.Read(_currentPath);
            if (_cachedCurrent is not null && _cachedCurrentFingerprint == fingerprint)
                return _cachedCurrent;

            await using var stream = new FileStream(_currentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
            var snapshot = await JsonSerializer.DeserializeAsync<SaveIndexSnapshot>(stream, JsonOptions, cancellationToken);
            if (snapshot is null) return null;
            var normalized = NormalizeSnapshot(snapshot);
            _cachedCurrent = normalized;
            _cachedCurrentFingerprint = fingerprint;
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PublishAsync(SaveIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var versionedPath = Path.Combine(_snapshotsRoot, SafeFileName(snapshot.SnapshotId) + ".json");
            var versionedTemporary = versionedPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await WriteJsonAsync(versionedTemporary, snapshot, cancellationToken);
            File.Move(versionedTemporary, versionedPath, overwrite: true);

            var currentTemporary = _currentPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await WriteJsonAsync(currentTemporary, snapshot, cancellationToken);
            File.Move(currentTemporary, _currentPath, overwrite: true);
            UpdateCurrentCache(NormalizeSnapshot(snapshot));
            TrimVersionedSnapshots(3);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordFailureAsync(SaveIndexFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(failure, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(_failurePath, line, cancellationToken);
            TrimFailureLines(100);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveIndexRepositoryHistory> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = new List<SaveIndexHistoryEntry>();
            foreach (var path in Directory.EnumerateFiles(_snapshotsRoot, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
                    var snapshot = await JsonSerializer.DeserializeAsync<SaveIndexSnapshot>(stream, JsonOptions, cancellationToken);
                    if (snapshot is null) continue;
                    snapshot = NormalizeSnapshot(snapshot);
                    entries.Add(new SaveIndexHistoryEntry(
                        snapshot.SnapshotId,
                        snapshot.WorldId,
                        snapshot.CreatedAt,
                        snapshot.ParsedAt,
                        Math.Max(0, (long)(snapshot.ParsedAt - snapshot.CreatedAt).TotalMilliseconds),
                        true,
                        snapshot.LevelSha256,
                        0,
                        snapshot.Players.Count,
                        snapshot.Items.Count,
                        snapshot.Pals.Count,
                        snapshot.Guilds.Count,
                        snapshot.Bases.Count,
                        null));
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A partially copied history file must not make the current index unavailable.
                }
            }

            var failures = new List<SaveIndexFailure>();
            if (File.Exists(_failurePath))
            {
                foreach (var line in await File.ReadAllLinesAsync(_failurePath, cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var failure = JsonSerializer.Deserialize<SaveIndexFailure>(line, JsonOptions);
                        if (failure is not null) failures.Add(failure);
                    }
                    catch (JsonException)
                    {
                        // Ignore one damaged history line.
                    }
                }
            }

            return new SaveIndexRepositoryHistory(entries, failures.OrderByDescending(x => x.FailedAt).ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SaveIndexSnapshot>> GetRecentSnapshotsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshots = new List<SaveIndexSnapshot>(safeLimit);
            foreach (var path in Directory.EnumerateFiles(_snapshotsRoot, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
                    var snapshot = await JsonSerializer.DeserializeAsync<SaveIndexSnapshot>(stream, JsonOptions, cancellationToken);
                    if (snapshot is null) continue;
                    snapshots.Add(NormalizeSnapshot(snapshot));
                    if (snapshots.Count >= safeLimit) break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // One damaged or inaccessible historical index must not block backfill of the remaining snapshots.
                }
            }
            return snapshots
                .OrderByDescending(snapshot => snapshot.ParsedAt)
                .ThenBy(snapshot => snapshot.SnapshotId, StringComparer.OrdinalIgnoreCase)
                .Take(safeLimit)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }


    private void UpdateCurrentCache(SaveIndexSnapshot snapshot)
    {
        _cachedCurrent = snapshot;
        _cachedCurrentFingerprint = CurrentFileFingerprint.Read(_currentPath);
    }

    private readonly record struct CurrentFileFingerprint(long Length, DateTime LastWriteTimeUtc)
    {
        public static CurrentFileFingerprint Read(string path)
        {
            var info = new FileInfo(path);
            return new CurrentFileFingerprint(info.Length, info.LastWriteTimeUtc);
        }
    }

    private static SaveIndexSnapshot NormalizeSnapshot(SaveIndexSnapshot snapshot)
        => snapshot with
        {
            Bases = snapshot.Bases.Select(baseCamp => baseCamp with
            {
                AssociationType = string.IsNullOrWhiteSpace(baseCamp.AssociationType) ? "direct" : baseCamp.AssociationType,
                PositionSource = string.IsNullOrWhiteSpace(baseCamp.PositionSource) ? "direct" : baseCamp.PositionSource,
                RelatedPlayerUids = baseCamp.RelatedPlayerUids ?? []
            }).ToArray()
        };

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private void TrimVersionedSnapshots(int keep)
    {
        foreach (var path in Directory.EnumerateFiles(_snapshotsRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(keep))
        {
            TryDelete(path);
        }
    }

    private void TrimFailureLines(int keep)
    {
        if (!File.Exists(_failurePath)) return;
        var lines = File.ReadLines(_failurePath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(keep).ToArray();
        File.WriteAllLines(_failurePath, lines);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class SaveIndexProgressTracker
{
    private readonly object _sync = new();
    private SaveIndexStatus _current = SaveIndexStatus.Idle();

    public SaveIndexStatus Current
    {
        get { lock (_sync) return _current; }
    }

    public void Begin(string snapshotId, DateTimeOffset startedAt)
    {
        lock (_sync)
        {
            _current = new SaveIndexStatus(
                SaveIndexState.WaitingForStableFile,
                "waitingForStableFile",
                1,
                startedAt,
                null,
                snapshotId,
                _current.LastSuccessfulSnapshotId,
                _current.LastSuccessfulAt,
                _current.LastSuccessfulSnapshotId is not null,
                null,
                true);
        }
    }

    public void Report(SaveIndexState state, string stage, int progress)
    {
        lock (_sync)
        {
            _current = _current with
            {
                State = state,
                Stage = stage,
                Progress = Math.Max(_current.Progress, Math.Clamp(progress, 0, 99))
            };
        }
    }

    public void Complete(string snapshotId, DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            _current = _current with
            {
                State = SaveIndexState.Completed,
                Stage = "completed",
                Progress = 100,
                CompletedAt = completedAt,
                CurrentSnapshotId = null,
                LastSuccessfulSnapshotId = snapshotId,
                LastSuccessfulAt = completedAt,
                UsingStaleSnapshot = false,
                Error = null,
                CanCancel = false
            };
        }
    }

    public void Fail(string error, DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            _current = _current with
            {
                State = SaveIndexState.Failed,
                Stage = "failed",
                CompletedAt = completedAt,
                CurrentSnapshotId = null,
                UsingStaleSnapshot = _current.LastSuccessfulSnapshotId is not null,
                Error = error,
                CanCancel = false
            };
        }
    }

    public void Cancel(DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            _current = _current with
            {
                State = SaveIndexState.Cancelled,
                Stage = "cancelled",
                CompletedAt = completedAt,
                CurrentSnapshotId = null,
                UsingStaleSnapshot = _current.LastSuccessfulSnapshotId is not null,
                Error = null,
                CanCancel = false
            };
        }
    }
}
