using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.Events;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Notifications;

public interface IWebhookTemplateRenderer
{
    RenderedWebhookMessage Render(WebhookChannel channel, PalOpsEvent palOpsEvent, IReadOnlyDictionary<string, string> secrets);
    IReadOnlyList<string> GetAvailableVariables(string eventType);
}

public sealed partial class WebhookTemplateRenderer : IWebhookTemplateRenderer
{
    private static readonly string[] CommonVariables =
    [
        "event.id", "event.type", "event.name", "event.time", "event.severity",
        "server.name", "server.state", "server.processId", "server.executablePath", "server.operationId",
        "player.name", "player.uid", "player.userId", "player.guildName",
        "backup.fileName", "backup.size", "backup.worldId", "backup.note",
        "system.cpuPercent", "system.memoryPercent", "system.diskFreeBytes",
        "metadata.message", "metadata.errorCode", "metadata.currentVersion", "metadata.latestVersion", "metadata.releaseUrl"
    ];

    public RenderedWebhookMessage Render(
        WebhookChannel channel,
        PalOpsEvent palOpsEvent,
        IReadOnlyDictionary<string, string> secrets)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = BuildRoots(palOpsEvent, secrets);
        var title = RenderText(channel.TitleTemplate, roots, used, false);
        var body = RenderText(channel.BodyTemplate, roots, used, false);
        var payload = RenderText(channel.PayloadTemplate, roots, used, true);

        if (title.Length > 300)
            throw new PalOpsApiException(422, "WEBHOOK_TEMPLATE_TOO_LARGE", "Webhook 标题渲染后超过 300 个字符。");
        if (Encoding.UTF8.GetByteCount(body) > 16 * 1024)
            throw new PalOpsApiException(422, "WEBHOOK_TEMPLATE_TOO_LARGE", "Webhook 正文渲染后超过 16 KiB。");
        if (Encoding.UTF8.GetByteCount(payload) > 64 * 1024)
            throw new PalOpsApiException(422, "WEBHOOK_TEMPLATE_TOO_LARGE", "Webhook JSON 渲染后超过 64 KiB。");
        try { using var _ = JsonDocument.Parse(payload); }
        catch (JsonException ex)
        {
            throw new PalOpsApiException(422, "WEBHOOK_PAYLOAD_JSON_INVALID", "Webhook Payload 模板渲染后不是有效 JSON。", ex.Message, null, ex);
        }
        return new(title, body, payload, used.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public IReadOnlyList<string> GetAvailableVariables(string eventType) => CommonVariables;

    private static IReadOnlyDictionary<string, object?> BuildRoots(PalOpsEvent palOpsEvent, IReadOnlyDictionary<string, string> secrets) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["event"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = palOpsEvent.EventId,
                ["type"] = palOpsEvent.EventType,
                ["name"] = palOpsEvent.EventType,
                ["time"] = palOpsEvent.OccurredAt,
                ["severity"] = palOpsEvent.Severity
            },
            ["server"] = palOpsEvent.Server,
            ["player"] = palOpsEvent.Player,
            ["backup"] = palOpsEvent.Backup,
            ["system"] = palOpsEvent.System,
            ["metadata"] = palOpsEvent.Metadata,
            ["secret"] = secrets
        };

    private static string RenderText(
        string template,
        IReadOnlyDictionary<string, object?> roots,
        ISet<string> used,
        bool jsonEscape)
    {
        template ??= string.Empty;
        return TokenRegex().Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            if (!TryResolve(roots, path, out var value))
                throw new PalOpsApiException(422, "WEBHOOK_TEMPLATE_VARIABLE_UNKNOWN", $"Webhook 模板包含未知变量：{path}", path);
            used.Add(path);
            var formatted = Format(value);
            return jsonEscape ? EscapeJsonStringContent(formatted) : formatted;
        });
    }

    private static bool TryResolve(IReadOnlyDictionary<string, object?> roots, string path, out object? value)
    {
        value = roots;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value is IReadOnlyDictionary<string, object?> objectDictionary)
            {
                var pair = objectDictionary.FirstOrDefault(item => item.Key.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (pair.Key is null) return false;
                value = pair.Value;
            }
            else if (value is IReadOnlyDictionary<string, string> stringDictionary)
            {
                var pair = stringDictionary.FirstOrDefault(item => item.Key.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (pair.Key is null) return false;
                value = pair.Value;
            }
            else return false;
        }
        return true;
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        string text => text,
        _ => JsonSerializer.Serialize(value)
    };

    private static string EscapeJsonStringContent(string value)
    {
        var serialized = JsonSerializer.Serialize(value);
        return serialized.Length >= 2 ? serialized[1..^1] : string.Empty;
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.-]{1,100})\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
