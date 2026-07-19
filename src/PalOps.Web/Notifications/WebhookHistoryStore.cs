using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Notifications;

public interface IWebhookHistoryStore
{
    Task AppendAsync(WebhookDeliveryRecord record, CancellationToken cancellationToken = default);
    Task<WebhookHistoryQueryResult> ListAsync(
        int page,
        int pageSize,
        string? status,
        string? channelId,
        string? eventType,
        CancellationToken cancellationToken = default);
}

public sealed partial class WebhookHistoryStore : IWebhookHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WebhookHistoryStore(IRuntimePathResolver paths)
    {
        _directory = paths.ResolveDataPath("webhook-history");
        Directory.CreateDirectory(_directory);
    }

    public async Task AppendAsync(WebhookDeliveryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var sanitized = record with
        {
            RequestSummary = Sanitize(record.RequestSummary, 2000),
            ResponseSummary = Sanitize(record.ResponseSummary, 2000),
            ErrorMessage = record.ErrorMessage is null ? null : Sanitize(record.ErrorMessage, 1000)
        };
        var path = Path.Combine(_directory, sanitized.CreatedAt.UtcDateTime.ToString("yyyy-MM") + ".jsonl");
        var line = JsonSerializer.Serialize(sanitized, JsonOptions) + Environment.NewLine;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024, true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WebhookHistoryQueryResult> ListAsync(
        int page,
        int pageSize,
        string? status,
        string? channelId,
        string? eventType,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var skip = checked((page - 1) * pageSize);
        var required = checked(skip + pageSize + 1);
        var matches = new List<WebhookDeliveryRecord>(required);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "????-??.jsonl")
                         .OrderByDescending(static value => value, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = await File.ReadAllLinesAsync(file, cancellationToken);
                for (var index = lines.Length - 1; index >= 0; index--)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    WebhookDeliveryRecord? record;
                    try { record = JsonSerializer.Deserialize<WebhookDeliveryRecord>(lines[index], JsonOptions); }
                    catch (JsonException) { continue; }
                    if (record is null || !Matches(record, status, channelId, eventType)) continue;
                    matches.Add(record);
                    if (matches.Count >= required) break;
                }
                if (matches.Count >= required) break;
            }
        }
        finally
        {
            _gate.Release();
        }

        var pageItems = matches.Skip(skip).Take(pageSize).ToArray();
        return new WebhookHistoryQueryResult(pageItems, matches.Count > skip + pageSize);
    }

    public static string BuildRequestSummary(HttpMethod method, Uri uri, int bodyBytes, string? providerType = null)
    {
        var path = providerType?.Equals(WebhookProviderTypes.Telegram, StringComparison.OrdinalIgnoreCase) == true
            ? "/bot[REDACTED]/sendMessage"
            : uri.AbsolutePath;
        return $"{method.Method.ToUpperInvariant()} {uri.Scheme}://{uri.Host}{path} body={Math.Max(0, bodyBytes)}B";
    }

    private static bool Matches(WebhookDeliveryRecord record, string? status, string? channelId, string? eventType) =>
        (string.IsNullOrWhiteSpace(status) || record.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(channelId) || record.ChannelId.Equals(channelId, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(eventType) || record.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string value, int maxLength)
    {
        var result = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        result = BearerRegex().Replace(result, "$1[REDACTED]");
        result = SecretAssignmentRegex().Replace(result, "$1=[REDACTED]");
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    [GeneratedRegex(@"(?i)(authorization\s*:\s*bearer\s+)[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)(token|secret|password|signature|sign|key)\s*=\s*[^&\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();
}
