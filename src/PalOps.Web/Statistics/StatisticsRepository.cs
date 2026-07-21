using System.Text;
using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Statistics;

public interface IStatisticsRepository
{
    Task AppendAsync(StatisticsMetricPoint point, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsMetricPoint>> QueryAsync(
        StatisticsGranularity granularity,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsRetentionTierStatus>> GetRetentionStatusAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task ApplyRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class JsonlStatisticsRepository : IStatisticsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonlStatisticsRepository> _logger;

    public JsonlStatisticsRepository(IRuntimePathResolver paths, ILogger<JsonlStatisticsRepository> logger)
    {
        _root = paths.ResolveDataPath("statistics");
        _logger = logger;
        foreach (var name in new[] { "raw", "minute", "quarter", "daily" })
            Directory.CreateDirectory(Path.Combine(_root, name));
    }

    public async Task AppendAsync(StatisticsMetricPoint point, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(point);
        var path = ShardPath(point);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(point, JsonOptions) + Environment.NewLine;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using var writer = new StreamWriter(stream, Utf8NoBom, 64 * 1024, leaveOpen: true);
            await writer.WriteAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StatisticsMetricPoint>> QueryAsync(
        StatisticsGranularity granularity,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to <= from) throw new ArgumentException("Statistics query end must be after start.");
        limit = Math.Clamp(limit, 1, 10_000);
        var byBucket = new Dictionary<string, StatisticsMetricPoint>(StringComparer.Ordinal);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in EnumerateShardFiles(granularity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReadFileAsync(file, point =>
                {
                    if (point.Granularity != granularity) return;
                    if (point.BucketEnd <= from || point.BucketStart >= to) return;
                    UpsertLatest(byBucket, point);
                }, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        var result = byBucket.Values
            .OrderBy(static point => point.BucketStart)
            .ToArray();
        if (result.Length > limit)
            throw new ArgumentException($"Statistics query returned {result.Length} points, exceeding the {limit} point limit.");
        return result;
    }

    public async Task<IReadOnlyList<StatisticsRetentionTierStatus>> GetRetentionStatusAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var result = new List<StatisticsRetentionTierStatus>(4);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var granularity in Enum.GetValues<StatisticsGranularity>())
            {
                var byBucket = new Dictionary<string, StatisticsMetricPoint>(StringComparer.Ordinal);
                var files = EnumerateShardFiles(granularity).ToArray();
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ReadFileAsync(file, point =>
                    {
                        if (point.Granularity == granularity) UpsertLatest(byBucket, point);
                    }, cancellationToken);
                }
                var values = byBucket.Values.ToArray();
                result.Add(new(
                    granularity,
                    StatisticsRetentionPolicy.Cutoff(granularity, now),
                    values.Length == 0 ? null : values.Min(static point => point.BucketStart),
                    values.Length == 0 ? null : values.Max(static point => point.BucketEnd),
                    files.Length,
                    values.LongLength));
            }
        }
        finally
        {
            _gate.Release();
        }
        return result;
    }

    public async Task ApplyRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var granularity in Enum.GetValues<StatisticsGranularity>())
            {
                var cutoff = StatisticsRetentionPolicy.Cutoff(granularity, now);
                if (!cutoff.HasValue) continue;
                foreach (var file in EnumerateShardFiles(granularity).ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var validRecordCount = 0;
                    var byBucket = new Dictionary<string, StatisticsMetricPoint>(StringComparer.Ordinal);
                    await ReadFileAsync(file, point =>
                    {
                        if (point.Granularity != granularity) return;
                        validRecordCount++;
                        UpsertLatest(byBucket, point);
                    }, cancellationToken);

                    var retained = byBucket.Values
                        .Where(point => point.BucketEnd > cutoff.Value)
                        .OrderBy(static point => point.BucketStart)
                        .ToArray();

                    if (retained.Length == 0)
                    {
                        if (validRecordCount > 0 || File.GetLastWriteTimeUtc(file) < cutoff.Value.UtcDateTime)
                            DeleteExpiredShard(file);
                        continue;
                    }

                    // Boundary shards can contain both expired and retained records (for example,
                    // a monthly 15-minute shard). Rewrite them atomically so the configured
                    // retention window is exact instead of waiting for the whole file to expire.
                    if (retained.Length != validRecordCount)
                        await RewriteShardAsync(file, retained, cancellationToken);
                }
            }
            RemoveEmptyDirectories(_root);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RewriteShardAsync(
        string file,
        IReadOnlyCollection<StatisticsMetricPoint> points,
        CancellationToken cancellationToken)
    {
        if (!IsSafeShardPath(file)) throw new InvalidOperationException("Statistics shard path escaped the data root.");
        var directory = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(file)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, Utf8NoBom, 64 * 1024, leaveOpen: true))
            {
                foreach (var point in points)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = JsonSerializer.Serialize(point, JsonOptions) + Environment.NewLine;
                    await writer.WriteAsync(line.AsMemory(), cancellationToken);
                }
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, file, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to remove temporary statistics shard {Path}.", temporary);
            }
        }
    }

    private void DeleteExpiredShard(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to delete expired statistics shard {Path}.", file);
        }
    }

    private string ShardPath(StatisticsMetricPoint point)
    {
        var utc = point.BucketStart.UtcDateTime;
        return point.Granularity switch
        {
            StatisticsGranularity.Raw => Path.Combine(_root, "raw", utc.ToString("yyyy-MM-dd"), utc.ToString("HH") + ".jsonl"),
            StatisticsGranularity.Minute => Path.Combine(_root, "minute", utc.ToString("yyyy-MM-dd") + ".jsonl"),
            StatisticsGranularity.QuarterHour => Path.Combine(_root, "quarter", utc.ToString("yyyy-MM") + ".jsonl"),
            StatisticsGranularity.Daily => Path.Combine(_root, "daily", utc.ToString("yyyy") + ".jsonl"),
            _ => throw new ArgumentOutOfRangeException(nameof(point), point.Granularity, null)
        };
    }

    private IEnumerable<string> EnumerateShardFiles(StatisticsGranularity granularity)
    {
        var directory = Path.Combine(_root, TierDirectory(granularity));
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
                .Where(IsSafeShardPath)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            : [];
    }

    private bool IsSafeShardPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Path.GetExtension(full).Equals(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ReadFileAsync(
        string file,
        Action<StatisticsMetricPoint> onPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<StatisticsMetricPoint>(line, JsonOptions) is { } point)
                        onPoint(point);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping corrupt statistics line in {Path}.", file);
                }
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void UpsertLatest(
        IDictionary<string, StatisticsMetricPoint> byBucket,
        StatisticsMetricPoint point)
    {
        if (!byBucket.TryGetValue(point.BucketId, out var current)
            || point.UpdatedAt >= current.UpdatedAt)
            byBucket[point.BucketId] = point;
    }

    private static string TierDirectory(StatisticsGranularity granularity) => granularity switch
    {
        StatisticsGranularity.Raw => "raw",
        StatisticsGranularity.Minute => "minute",
        StatisticsGranularity.QuarterHour => "quarter",
        StatisticsGranularity.Daily => "daily",
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
    };

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static value => value.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
