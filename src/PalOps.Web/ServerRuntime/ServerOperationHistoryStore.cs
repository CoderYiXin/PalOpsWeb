using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.ServerRuntime;

public interface IServerOperationHistoryStore
{
    Task AppendAsync(ServerOperationHistoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServerOperationHistoryRecord>> ListAsync(int limit, CancellationToken cancellationToken = default);
}

public sealed class ServerOperationHistoryStore : IServerOperationHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly int _maximum;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServerOperationHistoryStore(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        var data = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        _directory = Path.Combine(data, "server-operation-history");
        Directory.CreateDirectory(_directory);
        _maximum = Math.Clamp(options.Value.RuntimeHistoryLimit, 100, 10_000);
    }

    public async Task AppendAsync(ServerOperationHistoryRecord record, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, record.CompletedAt.ToString("yyyy-MM") + ".jsonl");
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ServerOperationHistoryRecord>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, _maximum);
        var records = new List<ServerOperationHistoryRecord>(limit);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.jsonl")
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = await File.ReadAllLinesAsync(path, cancellationToken);
                for (var index = lines.Length - 1; index >= 0 && records.Count < limit; index--)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    try
                    {
                        if (JsonSerializer.Deserialize<ServerOperationHistoryRecord>(lines[index], JsonOptions) is { } record)
                            records.Add(record);
                    }
                    catch (JsonException) { }
                }
                if (records.Count >= limit) break;
            }
        }
        finally
        {
            _gate.Release();
        }
        return records;
    }
}
