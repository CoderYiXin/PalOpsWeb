using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Backups;

public interface IBackupRepository
{
    Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<BackupRecord?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertAsync(BackupRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class JsonBackupRepository : IBackupRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonBackupRepository(IRuntimePathResolver paths)
    {
        var directory = paths.ResolveDataPath("backups");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "index.json");
    }

    public async Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadWithoutLockAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<BackupRecord?> FindAsync(string id, CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken)).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task UpsertAsync(BackupRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadWithoutLockAsync(cancellationToken)).ToList();
            var index = items.FindIndex(x => x.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) items[index] = record; else items.Add(record);
            await WriteWithoutLockAsync(items.OrderByDescending(x => x.CreatedAt).ToArray(), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadWithoutLockAsync(cancellationToken))
                .Where(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            await WriteWithoutLockAsync(items, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<BackupRecord>> ReadWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync<List<BackupRecord>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteWithoutLockAsync(IReadOnlyList<BackupRecord> items, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, _path, true);
    }
}
