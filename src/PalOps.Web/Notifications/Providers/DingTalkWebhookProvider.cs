using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalOps.Web.Notifications.Providers;

public sealed class DingTalkWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.DingTalk;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.DingTalk, "钉钉机器人", ["signingSecret"]);

    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets)
    {
        var uri = new Uri(channel.Url, UriKind.Absolute);
        if (secrets.TryGetValue("signingSecret", out var secret) && !string.IsNullOrWhiteSpace(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "\n" + secret)));
            uri = AppendQuery(uri, "timestamp", timestamp, "sign", sign);
        }
        var body = JsonSerializer.Serialize(new
        {
            msgtype = "markdown",
            markdown = new { title = message.Title, text = $"### {message.Title}\n{message.Body}" }
        });
        return new(HttpMethod.Post, uri, new Dictionary<string, string>(), "application/json", body);
    }

    private static Uri AppendQuery(Uri uri, params string[] values)
    {
        var query = uri.Query.TrimStart('?');
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            parts.Add(query);
        }

        for (var index = 0; index < values.Length; index += 2)
        {
            parts.Add(Uri.EscapeDataString(values[index]) + "=" + Uri.EscapeDataString(values[index + 1]));
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", parts) };
        return builder.Uri;
    }
}
