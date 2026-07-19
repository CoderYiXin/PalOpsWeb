using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Notifications.Providers;

public sealed partial class TelegramWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.Telegram;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.Telegram, "Telegram Bot", ["botToken", "chatId"]);
    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets)
    {
        if (!secrets.TryGetValue("botToken", out var token) || !BotTokenRegex().IsMatch(token))
            throw new PalOpsApiException(422, "WEBHOOK_SECRET_REQUIRED", "Telegram 渠道需要有效的 botToken。");
        if (!secrets.TryGetValue("chatId", out var chatId) || string.IsNullOrWhiteSpace(chatId))
            throw new PalOpsApiException(422, "WEBHOOK_SECRET_REQUIRED", "Telegram 渠道需要 chatId。");
        var uri = new Uri($"https://api.telegram.org/bot{token}/sendMessage", UriKind.Absolute);
        var payload = new { chat_id = chatId, text = $"{message.Title}\n{message.Body}", disable_web_page_preview = true };
        return new(HttpMethod.Post, uri, new Dictionary<string, string>(), "application/json", JsonSerializer.Serialize(payload));
    }

    [GeneratedRegex(@"^[0-9]+:[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant)]
    private static partial Regex BotTokenRegex();
}
