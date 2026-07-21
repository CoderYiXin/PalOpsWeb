using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.PlayerDiscipline;

public interface IPlayerDisciplineRepository
{
    Task<PlayerDisciplineState> ReadAsync(CancellationToken cancellationToken = default);
    Task<TResult> UpdateAsync<TResult>(Func<PlayerDisciplineState, TResult> update, CancellationToken cancellationToken = default);
    Task<bool> UpdateIfChangedAsync(Func<PlayerDisciplineState, bool> update, CancellationToken cancellationToken = default);
}

public sealed class JsonPlayerDisciplineRepository : IPlayerDisciplineRepository
{
    public const int MaximumAuditEntries = 2_000;
    private const int MaximumViolations = 10_000;
    private const int MaximumKickEntries = 2_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPlayerDisciplineRepository(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        var directory = Path.Combine(dataDirectory, "player-discipline");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
    }

    public async Task<PlayerDisciplineState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<TResult> UpdateAsync<TResult>(Func<PlayerDisciplineState, TResult> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var result = update(state);
            Normalize(state);
            await SaveUnsafeAsync(state, cancellationToken);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> UpdateIfChangedAsync(Func<PlayerDisciplineState, bool> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (!update(state)) return false;
            Normalize(state);
            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<PlayerDisciplineState> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new PlayerDisciplineState();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<PlayerDisciplineState>(stream, JsonOptions, cancellationToken) ?? new PlayerDisciplineState();
            Normalize(state);
            return state;
        }
        catch (JsonException)
        {
            var corrupt = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(_path, corrupt, overwrite: false);
            return new PlayerDisciplineState();
        }
    }

    private async Task SaveUnsafeAsync(PlayerDisciplineState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(json);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, TValue> NormalizeDictionary<TValue>(Dictionary<string, TValue>? source)
    {
        var normalized = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return normalized;
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && pair.Value is not null) normalized[key] = pair.Value;
        }
        return normalized;
    }

    private static void Normalize(PlayerDisciplineState state)
    {
        state.SchemaVersion = 1;
        state.Whitelist ??= new(StringComparer.OrdinalIgnoreCase);
        state.Bans ??= new(StringComparer.OrdinalIgnoreCase);
        state.Identities ??= new(StringComparer.OrdinalIgnoreCase);
        state.Violations ??= [];
        state.Kicks ??= [];
        state.Operations ??= [];
        state.Whitelist = NormalizeDictionary(state.Whitelist);
        state.Bans = NormalizeDictionary(state.Bans);
        state.Identities = NormalizeDictionary(state.Identities);
        state.Violations = state.Violations.OrderByDescending(static item => item.CreatedAt).Take(MaximumViolations).ToList();
        state.Kicks = state.Kicks.Where(static item => !string.IsNullOrWhiteSpace(item.UserId))
            .OrderByDescending(static item => item.KickedAt).Take(MaximumKickEntries).ToList();
        state.Operations = state.Operations.OrderByDescending(static item => item.Timestamp).Take(MaximumAuditEntries).ToList();
        foreach (var identity in state.Identities.Values)
        {
            identity.Names = new HashSet<string>(identity.Names ?? [], StringComparer.OrdinalIgnoreCase);
            identity.IpAddresses = new HashSet<string>(identity.IpAddresses ?? [], StringComparer.OrdinalIgnoreCase);
            identity.PlayerUids = new HashSet<string>(identity.PlayerUids ?? [], StringComparer.OrdinalIgnoreCase);
            identity.GuildNames = new HashSet<string>(identity.GuildNames ?? [], StringComparer.OrdinalIgnoreCase);
        }
    }
}
