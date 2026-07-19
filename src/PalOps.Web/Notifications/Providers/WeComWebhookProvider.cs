using System.Text.Json;

namespace PalOps.Web.Notifications.Providers;

public sealed class WeComWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.WeCom;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.WeCom, "企业微信机器人");
    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets)
    {
        var body = JsonSerializer.Serialize(new { msgtype = "markdown", markdown = new { content = $"**{message.Title}**\n{message.Body}" } });
        return new(HttpMethod.Post, new Uri(channel.Url, UriKind.Absolute), new Dictionary<string, string>(), "application/json", body);
    }
}
