using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.PluginManagement;

public interface IPluginManagementRepository
{
    Task<PluginManagementState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PluginManagementState state, CancellationToken cancellationToken = default);
}

public sealed class PluginManagementRepository : IPluginManagementRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<PluginManagementRepository> _logger;

    public PluginManagementRepository(IRuntimePathResolver paths, ILogger<PluginManagementRepository> logger)
    {
        _logger = logger;
        var directory = paths.ResolveDataPath("plugin-management");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "state.json");
        _backupPath = _statePath + ".bak";
    }

    public async Task<PluginManagementState> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(PluginManagementState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            Normalize(state);
            temporary = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(_statePath)) File.Copy(_statePath, _backupPath, true);
            File.Move(temporary, _statePath, true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
            _gate.Release();
        }
    }

    private async Task<PluginManagementState> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            var recovered = await TryReadBackupAsync(cancellationToken);
            return recovered ?? NewState();
        }
        try
        {
            return await ReadFileAsync(_statePath, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var quarantine = _statePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            try { File.Move(_statePath, quarantine, true); }
            catch (Exception moveError) { _logger.LogWarning(moveError, "Unable to quarantine corrupt plugin-management state."); }
            var recovered = await TryReadBackupAsync(cancellationToken);
            if (recovered is not null)
            {
                _logger.LogWarning(ex, "Plugin-management state was corrupt and was recovered from the last backup.");
                return recovered;
            }
            _logger.LogWarning(ex, "Plugin-management state and backup were unusable and have been reset.");
            return NewState();
        }
    }

    private async Task<PluginManagementState?> TryReadBackupAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_backupPath)) return null;
        try
        {
            var state = await ReadFileAsync(_backupPath, cancellationToken);
            try { File.Copy(_backupPath, _statePath, true); }
            catch (Exception copyError) { _logger.LogWarning(copyError, "Unable to restore plugin-management backup to the primary state path."); }
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Plugin-management backup state is unusable.");
            return null;
        }
    }

    private static async Task<PluginManagementState> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var state = await JsonSerializer.DeserializeAsync<PluginManagementState>(stream, JsonOptions, cancellationToken);
        if (state is null || state.SchemaVersion != 1)
            throw new InvalidDataException("插件管理状态版本不受支持。");
        Normalize(state);
        return state;
    }

    private static PluginManagementState NewState() => new();

    private static void Normalize(PluginManagementState state)
    {
        state.SchemaVersion = 1;
        state.Packages = new Dictionary<string, ManagedPluginRegistration>(state.Packages ?? new Dictionary<string, ManagedPluginRegistration>(), StringComparer.OrdinalIgnoreCase);
        state.Releases = new Dictionary<string, PluginReleaseStatus>(state.Releases ?? new Dictionary<string, PluginReleaseStatus>(), StringComparer.OrdinalIgnoreCase);
        state.Backups ??= [];
        state.History ??= [];
        state.Backups = state.Backups
            .OrderByDescending(static item => item.CreatedAt)
            .Take(100)
            .ToList();
        state.History = state.History
            .OrderByDescending(static item => item.CompletedAt)
            .Take(1000)
            .ToList();
    }
}
