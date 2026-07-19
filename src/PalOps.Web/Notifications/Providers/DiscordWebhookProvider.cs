using System.Text.Json;

namespace PalOps.Web.Notifications.Providers;

public sealed class DiscordWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.Discord;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.Discord, "Discord Webhook");
    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets)
    {
        var title = Limit(message.Title, 256);
        var body = Limit(message.Body, 4096);
        var payload = new { content = string.Empty, embeds = new[] { new { title, description = body } }, allowed_mentions = new { parse = Array.Empty<string>() } };
        return new(HttpMethod.Post, new Uri(channel.Url, UriKind.Absolute), new Dictionary<string, string>(), "application/json", JsonSerializer.Serialize(payload));
    }
    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
