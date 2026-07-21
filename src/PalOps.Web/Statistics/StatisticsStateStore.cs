using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Statistics;

public interface IStatisticsStateStore
{
    Task<StatisticsRecorderState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StatisticsRecorderState state, CancellationToken cancellationToken = default);
}

public sealed class StatisticsStateStore : IStatisticsStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<StatisticsStateStore> _logger;

    public StatisticsStateStore(IRuntimePathResolver paths, ILogger<StatisticsStateStore> logger)
    {
        var directory = paths.ResolveDataPath("statistics");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
        _logger = logger;
    }

    public async Task<StatisticsRecorderState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return StatisticsRecorderState.Empty();
            try
            {
                await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync<StatisticsRecorderState>(stream, JsonOptions, cancellationToken)
                       ?? StatisticsRecorderState.Empty();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                var corruptPath = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
                try { File.Move(_path, corruptPath, false); }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(moveEx, "Unable to quarantine corrupt statistics state {Path}.", _path);
                }
                _logger.LogWarning(ex, "Statistics state was corrupt and has been reset. Quarantined path: {Path}.", corruptPath);
                return StatisticsRecorderState.Empty();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(StatisticsRecorderState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await using (var stream = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporary, _path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
