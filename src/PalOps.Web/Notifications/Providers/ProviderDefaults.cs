namespace PalOps.Web.Notifications.Providers;

internal static class ProviderDefaults
{
    public static WebhookProviderDefinition Definition(string type, string name, IReadOnlyList<string>? secretFields = null) => new(
        type,
        name,
        true,
        false,
        secretFields ?? [],
        "{{event.name}}",
        "{{metadata.message}}",
        "{}");
}
