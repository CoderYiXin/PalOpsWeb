using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;

namespace PalOps.Web.Audit;

public sealed record AuditRecord(DateTimeOffset Timestamp, string EventType, string Outcome, string RemoteIp, string Summary, JsonNode? Data);

public interface IAuditLogService
{
    Task WriteAsync(string eventType, string outcome, string remoteIp, string summary, object? data = null, CancellationToken cancellationToken = default);
    Task<AuditPageResponse> ReadAsync(int page, int pageSize, string? eventType, CancellationToken cancellationToken = default);
}

public sealed class AuditLogService : IAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuditLogService(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        _directory = Path.Combine(dataDirectory, "audit");
        Directory.CreateDirectory(_directory);
    }

    public async Task WriteAsync(string eventType, string outcome, string remoteIp, string summary, object? data = null, CancellationToken cancellationToken = default)
    {
        var node = data is null ? null : JsonSerializer.SerializeToNode(data, JsonOptions);
        var record = new AuditRecord(
            DateTimeOffset.UtcNow,
            Limit(eventType, 80),
            Limit(outcome, 40),
            Limit(remoteIp, 80),
            Limit(summary, 500),
            AuditRedactor.Redact(node));
        var line = JsonSerializer.Serialize(record, JsonOptions);
        var path = Path.Combine(_directory, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuditPageResponse> ReadAsync(int page, int pageSize, string? eventType, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var needed = checked(page * pageSize + 1);
        var records = new List<AuditRecord>(needed);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var files = Directory.EnumerateFiles(_directory, "*.jsonl")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file, cancellationToken);
                for (var index = lines.Length - 1; index >= 0; index--)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    try
                    {
                        var record = JsonSerializer.Deserialize<AuditRecord>(lines[index], JsonOptions);
                        if (record is null) continue;
                        if (!string.IsNullOrWhiteSpace(eventType) && !record.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase)) continue;
                        records.Add(record);
                        if (records.Count >= needed) break;
                    }
                    catch (JsonException)
                    {
                    }
                }
                if (records.Count >= needed) break;
            }
        }
        finally
        {
            _gate.Release();
        }

        var skip = (page - 1) * pageSize;
        var entries = records.Skip(skip).Take(pageSize)
            .Select(static record => new AuditEntryResponse(record.Timestamp, record.EventType, record.Outcome, record.RemoteIp, record.Summary, record.Data))
            .ToArray();
        return new AuditPageResponse(entries, page, pageSize, records.Count > skip + pageSize);
    }

    private static string Limit(string value, int length)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= length ? normalized : normalized[..length];
    }
}

public static class AuditRedactor
{
    private static readonly string[] SecretFragments = ["password", "token", "secret", "authorization", "credential"];

    public static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (SecretFragments.Any(fragment => property.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    obj[property.Key] = "[REDACTED]";
                }
                else
                {
                    // The child node is already attached to this JsonObject.
                    // Reassigning the same node would attempt to give it another parent and
                    // causes successful API operations to be reported as HTTP 500 while
                    // their audit payload is being redacted. Traverse it in place instead.
                    Redact(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            // Array items are also already parented by the current JsonArray. Redact each
            // child in place; only secret object properties need to be replaced.
            for (var index = 0; index < array.Count; index++) Redact(array[index]);
        }
        return node;
    }
}
