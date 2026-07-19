using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PalOps.Web.Infrastructure;

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

public interface ISystemLogStore
{
    Task<SystemLogPage> ReadAsync(int page, int pageSize, string? level, string? query, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed partial class FileSystemLoggerProvider : ILoggerProvider, IHostedService, ISystemLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly Channel<SystemLogRecord> _channel = Channel.CreateBounded<SystemLogRecord>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private CancellationTokenSource? _shutdown;
    private Task? _writerTask;

    public FileSystemLoggerProvider(IRuntimePathResolver paths)
    {
        _directory = paths.ResolveDataPath("logs");
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileSystemLogger(categoryName, _channel.Writer);
    public void Dispose() { _shutdown?.Cancel(); _shutdown?.Dispose(); _fileGate.Dispose(); }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _writerTask = Task.Run(() => WriterLoopAsync(_shutdown.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_writerTask is null) return;
        try { await _writerTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { _shutdown?.Cancel(); }
    }

    public async Task<SystemLogPage> ReadAsync(int page, int pageSize, string? level, string? query, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var skip = (page - 1) * pageSize;
        var needed = skip + pageSize + 1;
        var entries = new List<SystemLogRecord>(needed);
        var normalizedLevel = level?.Trim();
        var needle = query?.Trim();

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl").OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var lines = await File.ReadAllLinesAsync(path, cancellationToken);
                for (var index = lines.Length - 1; index >= 0 && entries.Count < needed; index--)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    try
                    {
                        var record = JsonSerializer.Deserialize<SystemLogRecord>(lines[index], JsonOptions);
                        if (record is null) continue;
                        if (Enum.TryParse<LogLevel>(record.Level, true, out var recordLevel)
                            && IsRoutineHttpNoise(record.Category, recordLevel)) continue;
                        if (!string.IsNullOrWhiteSpace(normalizedLevel) && !record.Level.Equals(normalizedLevel, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrWhiteSpace(needle) && !($"{record.Category} {record.Message} {record.Exception}".Contains(needle, StringComparison.OrdinalIgnoreCase))) continue;
                        entries.Add(record);
                    }
                    catch (JsonException) { }
                }
                if (entries.Count >= needed) break;
            }
        }
        finally { _fileGate.Release(); }

        return new SystemLogPage(entries.Skip(skip).Take(pageSize).ToArray(), page, pageSize, entries.Count > skip + pageSize);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken);
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

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var record in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var path = Path.Combine(_directory, record.Timestamp.ToString("yyyy-MM-dd") + ".jsonl");
                var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
                await _fileGate.WaitAsync(cancellationToken);
                try { await File.AppendAllTextAsync(path, line, cancellationToken); }
                finally { _fileGate.Release(); }
                TrimOldFiles(30);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { }
        }
    }

    private void TrimOldFiles(int keepDays)
    {
        var threshold = DateTime.UtcNow.Date.AddDays(-Math.Max(1, keepDays));
        foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl"))
        {
            try { if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path); } catch { }
        }
    }

    private sealed class FileSystemLogger(string category, ChannelWriter<SystemLogRecord> writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information && !IsRoutineHttpNoise(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = Redact(Limit(formatter(state, exception), 4000));
            var exceptionText = exception is null ? null : Redact(Limit(exception.ToString(), 12000));
            writer.TryWrite(new SystemLogRecord(DateTimeOffset.UtcNow, logLevel.ToString(), Limit(category, 300), eventId.Id, message, exceptionText));
        }
    }

    private static bool IsRoutineHttpNoise(string category, LogLevel logLevel) =>
        logLevel < LogLevel.Warning
        && (category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal)
            || category.StartsWith("Microsoft.Extensions.Http", StringComparison.Ordinal));

    private static string Redact(string value)
    {
        var redacted = BearerRegex().Replace(value, "Bearer [REDACTED]");
        redacted = SecretRegex().Replace(redacted, "$1=[REDACTED]");
        return redacted;
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+\-/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)(password|token|secret|authorization|api[_-]?key)\s*[=:]\s*[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();
}
