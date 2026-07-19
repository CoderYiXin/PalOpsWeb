namespace PalOps.Web.Notifications.Providers;

public interface IWebhookProvider
{
    string Type { get; }
    WebhookProviderDefinition Definition { get; }
    WebhookRequestDefinition CreateRequest(
        WebhookChannel channel,
        RenderedWebhookMessage message,
        IReadOnlyDictionary<string, string> secrets);
}
