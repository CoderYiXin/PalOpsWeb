using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PalOps.Web.Infrastructure;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Logging;

public sealed record SystemLogRecord(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    int EventId,
    string Message,
    string? Exception);

public sealed record SystemLogPage(
    IReadOnlyList<SystemLogRecord> Entries,
    int Page,
    int PageSize,
    bool HasMore);

public sealed record SystemLogQuery(
    int Page = 1,
    int PageSize = 100,
    string? Level = null,
    string? Category = null,
    string? Query = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool? HasException = null);

public sealed record SystemLogLevelCount(string Level, long Count);
public sealed record SystemLogCategoryCount(string Category, long Count);
public sealed record SystemLogSummary(
    long Total,
    long Errors,
    long Warnings,
    long WithException,
    DateTimeOffset? OldestAt,
    DateTimeOffset? NewestAt,
    IReadOnlyList<SystemLogLevelCount> Levels,
    IReadOnlyList<SystemLogCategoryCount> TopCategories,
    long DroppedEntries,
    DateTimeOffset GeneratedAt);

public sealed record SystemLogExport(string FileName, string ContentType, byte[] Content);
public sealed record SystemLogTelemetry(
    long WrittenEntries,
    long DroppedEntries,
    DateTimeOffset? LastWriteAt,
    int FileCount,
    long TotalSizeBytes,
    int RetentionDays);

public interface ISystemLogStore
{
    Task<SystemLogPage> ReadAsync(int page, int pageSize, string? level, string? query, CancellationToken cancellationToken = default);
    Task<SystemLogPage> ReadAsync(SystemLogQuery query, CancellationToken cancellationToken = default);
    Task<SystemLogSummary> GetSummaryAsync(SystemLogQuery? query = null, CancellationToken cancellationToken = default);
    Task<SystemLogExport> ExportAsync(SystemLogQuery query, string format, CancellationToken cancellationToken = default);
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
    SystemLogTelemetry GetTelemetry();
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed partial class FileSystemLoggerProvider : ILoggerProvider, IHostedService, ISystemLogStore
{
    private const int RetentionDays = 30;
    private const int ExportLimit = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly Channel<SystemLogRecord> _channel = Channel.CreateBounded<SystemLogRecord>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly IServiceProvider _services;
    private IBackgroundWorkerSupervisor? _supervisor;
    private CancellationTokenSource? _shutdown;
    private Task? _writerTask;
    private readonly TaskCompletionSource<bool> _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _writtenEntries;
    private long _droppedEntries;
    private long _lastWriteUnixMilliseconds;
    private long _lastRetentionSweepUnixMilliseconds;

    public FileSystemLoggerProvider(IRuntimePathResolver paths, IServiceProvider services)
    {
        _directory = paths.ResolveDataPath("logs");
        Directory.CreateDirectory(_directory);
        _services = services;
    }

    public ILogger CreateLogger(string categoryName) => new FileSystemLogger(categoryName, Write);

    public void Dispose()
    {
        _shutdown?.Cancel();
        _shutdown?.Dispose();
        _fileGate.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _supervisor = _services.GetRequiredService<IBackgroundWorkerSupervisor>();
        _writerTask = _supervisor.RunAsync("system-log-writer", WriterLoopAsync, _shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        try { await _drained.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _shutdown?.Cancel();
        if (_writerTask is null) return;
        try { await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    public Task<SystemLogPage> ReadAsync(
        int page,
        int pageSize,
        string? level,
        string? query,
        CancellationToken cancellationToken = default) =>
        ReadAsync(new SystemLogQuery(page, pageSize, level, Query: query), cancellationToken);

    public async Task<SystemLogPage> ReadAsync(SystemLogQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery(query);
        var skip = (normalized.Page - 1) * normalized.PageSize;
        var records = await ReadMatchingAsync(normalized, skip + normalized.PageSize + 1, cancellationToken).ConfigureAwait(false);
        return new(
            records.Skip(skip).Take(normalized.PageSize).ToArray(),
            normalized.Page,
            normalized.PageSize,
            records.Count > skip + normalized.PageSize);
    }

    public async Task<SystemLogSummary> GetSummaryAsync(SystemLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery((query ?? new SystemLogQuery()) with { Page = 1, PageSize = 200 });
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0, errors = 0, warnings = 0, withException = 0;
        DateTimeOffset? oldest = null, newest = null;

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in EnumerateLogFiles())
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    var record = Deserialize(line);
                    if (record is null || !Matches(record, normalized)) continue;
                    total++;
                    counts[record.Level] = counts.GetValueOrDefault(record.Level) + 1;
                    categories[record.Category] = categories.GetValueOrDefault(record.Category) + 1;
                    if (record.Level.Equals("Error", StringComparison.OrdinalIgnoreCase) || record.Level.Equals("Critical", StringComparison.OrdinalIgnoreCase)) errors++;
                    if (record.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)) warnings++;
                    if (!string.IsNullOrWhiteSpace(record.Exception)) withException++;
                    if (!oldest.HasValue || record.Timestamp < oldest) oldest = record.Timestamp;
                    if (!newest.HasValue || record.Timestamp > newest) newest = record.Timestamp;
                }
            }
        }
        finally { _fileGate.Release(); }

        return new(
            total,
            errors,
            warnings,
            withException,
            oldest,
            newest,
            counts.OrderByDescending(static item => item.Value).Select(static item => new SystemLogLevelCount(item.Key, item.Value)).ToArray(),
            categories.OrderByDescending(static item => item.Value).Take(10).Select(static item => new SystemLogCategoryCount(item.Key, item.Value)).ToArray(),
            Interlocked.Read(ref _droppedEntries),
            DateTimeOffset.UtcNow);
    }

    public async Task<SystemLogExport> ExportAsync(SystemLogQuery query, string format, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery(query with { Page = 1, PageSize = 200 });
        var records = await ReadMatchingAsync(normalized, ExportLimit, cancellationToken).ConfigureAwait(false);
        var normalizedFormat = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        if (normalizedFormat == "jsonl")
        {
            var content = string.Join(Environment.NewLine, records.Select(record => JsonSerializer.Serialize(record, JsonOptions)));
            return new($"palops-system-logs-{timestamp}.jsonl", "application/x-ndjson; charset=utf-8", Encoding.UTF8.GetBytes(content));
        }

        if (normalizedFormat != "csv") throw new ArgumentException("日志导出格式仅支持 csv 或 jsonl。", nameof(format));
        var builder = new StringBuilder("timestamp,level,category,eventId,message,exception\r\n");
        foreach (var record in records)
        {
            builder.Append(Csv(record.Timestamp.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(record.Level)).Append(',')
                .Append(Csv(record.Category)).Append(',')
                .Append(record.EventId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(record.Message)).Append(',')
                .Append(Csv(record.Exception ?? string.Empty)).Append("\r\n");
        }
        return new($"palops-system-logs-{timestamp}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(builder.ToString()));
    }

    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.GetLastWriteTimeUtc(path) >= before.UtcDateTime) continue;
                try { File.Delete(path); removed++; }
                catch (IOException) { }
            }
        }
        finally { _fileGate.Release(); }
        return removed;
    }

    public SystemLogTelemetry GetTelemetry()
    {
        var files = Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "*.jsonl").ToArray() : [];
        var lastWrite = Interlocked.Read(ref _lastWriteUnixMilliseconds);
        return new(
            Interlocked.Read(ref _writtenEntries),
            Interlocked.Read(ref _droppedEntries),
            lastWrite <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastWrite),
            files.Length,
            files.Sum(path => { try { return new FileInfo(path).Length; } catch { return 0L; } }),
            RetentionDays);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Delete(path); } catch (IOException) { }
            }
        }
        finally { _fileGate.Release(); }
    }

    private bool Write(SystemLogRecord record)
    {
        if (_channel.Writer.TryWrite(record)) return true;
        Interlocked.Increment(ref _droppedEntries);
        return false;
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _supervisor?.Heartbeat("system-log-writer");
            var readableTask = _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var heartbeatDelay = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            var completed = await Task.WhenAny(readableTask, heartbeatDelay).ConfigureAwait(false);
            if (completed == heartbeatDelay) continue;
            if (!await readableTask.ConfigureAwait(false))
            {
                _drained.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return;
            }

            var batches = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            var writtenEntries = 0;
            while (_channel.Reader.TryRead(out var record))
            {
                var path = Path.Combine(_directory, record.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                if (!batches.TryGetValue(path, out var builder))
                {
                    builder = new StringBuilder();
                    batches[path] = builder;
                }
                builder.AppendLine(JsonSerializer.Serialize(record, JsonOptions));
                writtenEntries++;
            }

            if (writtenEntries > 0)
            {
                await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (var batch in batches)
                        await File.AppendAllTextAsync(batch.Key, batch.Value.ToString(), cancellationToken).ConfigureAwait(false);
                }
                finally { _fileGate.Release(); }
                Interlocked.Add(ref _writtenEntries, writtenEntries);
                Interlocked.Exchange(ref _lastWriteUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            TrimOldFilesIfDue(RetentionDays);
        }
    }

    private async Task<IReadOnlyList<SystemLogRecord>> ReadMatchingAsync(SystemLogQuery query, int maximum, CancellationToken cancellationToken)
    {
        maximum = Math.Clamp(maximum, 1, ExportLimit);
        var output = new List<SystemLogRecord>(maximum);
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in EnumerateLogFiles())
            {
                var latestInFile = new Queue<SystemLogRecord>(maximum);
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    var record = Deserialize(line);
                    if (record is null || !Matches(record, query)) continue;
                    latestInFile.Enqueue(record);
                    if (latestInFile.Count > maximum) latestInFile.Dequeue();
                }
                foreach (var record in latestInFile.Reverse())
                {
                    output.Add(record);
                    if (output.Count >= maximum) return output;
                }
            }
        }
        finally { _fileGate.Release(); }
        return output;
    }

    private IEnumerable<string> EnumerateLogFiles() =>
        Directory.EnumerateFiles(_directory, "*.jsonl").OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase);

    private static SystemLogQuery NormalizeQuery(SystemLogQuery query) => query with
    {
        Page = Math.Max(1, query.Page),
        PageSize = Math.Clamp(query.PageSize, 1, 200),
        Level = Normalize(query.Level),
        Category = Normalize(query.Category),
        Query = Normalize(query.Query)
    };

    private static bool Matches(SystemLogRecord record, SystemLogQuery query)
    {
        if (Enum.TryParse<LogLevel>(record.Level, true, out var recordLevel) && IsRoutineHttpNoise(record.Category, recordLevel)) return false;
        if (!string.IsNullOrWhiteSpace(query.Level) && !record.Level.Equals(query.Level, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(query.Category) && !record.Category.Contains(query.Category, StringComparison.OrdinalIgnoreCase)) return false;
        if (query.From.HasValue && record.Timestamp < query.From.Value) return false;
        if (query.To.HasValue && record.Timestamp > query.To.Value) return false;
        if (query.HasException.HasValue && query.HasException.Value != !string.IsNullOrWhiteSpace(record.Exception)) return false;
        return string.IsNullOrWhiteSpace(query.Query)
               || $"{record.Category} {record.Message} {record.Exception}".Contains(query.Query, StringComparison.OrdinalIgnoreCase);
    }

    private static SystemLogRecord? Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<SystemLogRecord>(line, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private void TrimOldFilesIfDue(int keepDays)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var previous = Interlocked.Read(ref _lastRetentionSweepUnixMilliseconds);
        if (previous > 0 && now - previous < TimeSpan.FromHours(1).TotalMilliseconds) return;
        if (Interlocked.CompareExchange(ref _lastRetentionSweepUnixMilliseconds, now, previous) != previous) return;

        var threshold = DateTime.UtcNow.Date.AddDays(-Math.Max(1, keepDays));
        foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl"))
        {
            try { if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path); }
            catch { }
        }
    }

    private sealed class FileSystemLogger(string category, Func<SystemLogRecord, bool> writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && !IsRoutineHttpNoise(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = Redact(Limit(formatter(state, exception), 4000));
            var exceptionText = exception is null ? null : Redact(Limit(exception.ToString(), 12000));
            _ = writer(new SystemLogRecord(DateTimeOffset.UtcNow, logLevel.ToString(), Limit(category, 300), eventId.Id, message, exceptionText));
        }
    }

    private static bool IsRoutineHttpNoise(string category, LogLevel logLevel) =>
        logLevel < LogLevel.Warning
        && (category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal)
            || category.StartsWith("Microsoft.Extensions.Http", StringComparison.Ordinal));

    private static string Redact(string value)
    {
        var redacted = BearerRegex().Replace(value, "Bearer [REDACTED]");
        return SecretRegex().Replace(redacted, "$1=[REDACTED]");
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+\-/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)(password|token|secret|authorization|api[_-]?key)\s*[=:]\s*[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();
}
