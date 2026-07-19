using System.Text.Json;

namespace PalOps.Web.Notifications.Providers;

public sealed class SlackWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.Slack;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.Slack, "Slack Incoming Webhook");
    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets) =>
        new(HttpMethod.Post, new Uri(channel.Url, UriKind.Absolute), new Dictionary<string, string>(), "application/json",
            JsonSerializer.Serialize(new { text = $"*{message.Title}*\n{message.Body}" }));
}
