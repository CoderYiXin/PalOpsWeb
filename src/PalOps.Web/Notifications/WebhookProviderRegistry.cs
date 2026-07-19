using PalOps.Web.Notifications.Providers;

namespace PalOps.Web.Notifications;

public interface IWebhookProviderRegistry
{
    IReadOnlyList<WebhookProviderDefinition> GetDefinitions();
    IWebhookProvider Get(string providerType);
}

public sealed class WebhookProviderRegistry : IWebhookProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IWebhookProvider> _providers;
    private readonly IReadOnlyList<WebhookProviderDefinition> _definitions;

    public WebhookProviderRegistry(IEnumerable<IWebhookProvider> providers)
    {
        var dictionary = new Dictionary<string, IWebhookProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!dictionary.TryAdd(provider.Type, provider))
                throw new InvalidOperationException($"重复的 Webhook Provider 类型：{provider.Type}");
        }
        _providers = dictionary;
        _definitions = dictionary.Values.Select(provider => provider.Definition)
            .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<WebhookProviderDefinition> GetDefinitions() => _definitions;

    public IWebhookProvider Get(string providerType)
    {
        if (string.IsNullOrWhiteSpace(providerType) || !_providers.TryGetValue(providerType.Trim(), out var provider))
            throw new PalOps.Web.Infrastructure.PalOpsApiException(400, "WEBHOOK_PROVIDER_UNSUPPORTED", "不支持的 Webhook 渠道类型。", providerType);
        return provider;
    }
}
