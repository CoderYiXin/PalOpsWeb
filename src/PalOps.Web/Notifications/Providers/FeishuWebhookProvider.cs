using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalOps.Web.Notifications.Providers;

public sealed class FeishuWebhookProvider : IWebhookProvider
{
    public string Type => WebhookProviderTypes.Feishu;
    public WebhookProviderDefinition Definition { get; } = ProviderDefaults.Definition(WebhookProviderTypes.Feishu, "飞书机器人", ["signingSecret"]);

    public WebhookRequestDefinition CreateRequest(WebhookChannel channel, RenderedWebhookMessage message, IReadOnlyDictionary<string, string> secrets)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string? sign = null;
        if (secrets.TryGetValue("signingSecret", out var secret) && !string.IsNullOrWhiteSpace(secret))
        {
            var key = Encoding.UTF8.GetBytes(timestamp + "\n" + secret);
            using var hmac = new HMACSHA256(key);
            sign = Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
        }
        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "interactive",
            ["card"] = new
            {
                header = new { title = new { tag = "plain_text", content = message.Title } },
                elements = new[] { new { tag = "markdown", content = message.Body } }
            }
        };
        if (sign is not null) { payload["timestamp"] = timestamp; payload["sign"] = sign; }
        return new(HttpMethod.Post, new Uri(channel.Url, UriKind.Absolute), new Dictionary<string, string>(), "application/json", JsonSerializer.Serialize(payload));
    }
}
