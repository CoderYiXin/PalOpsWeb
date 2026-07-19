namespace PalOps.Web.Notifications.Providers;

public sealed class GenericJsonWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.GenericJson;
    public WebhookProviderDefinition Definition { get; } = new(
        WebhookProviderTypes.GenericJson,
        "通用 JSON Webhook",
        true,
        true,
        [],
        "{{event.name}}",
        "{{metadata.message}}",
        "{\"title\":\"{{event.name}}\",\"message\":\"{{metadata.message}}\",\"eventType\":\"{{event.type}}\",\"occurredAt\":\"{{event.time}}\"}");

    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets) =>
        new(new HttpMethod(channel.HttpMethod), new Uri(channel.Url, UriKind.Absolute), channel.Headers, "application/json", message.PayloadJson);
}
