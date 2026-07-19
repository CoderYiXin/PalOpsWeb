using System.Net;
using System.Net.Sockets;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Notifications;

public interface IWebhookDestinationValidator
{
    Task ValidateAsync(Uri uri, bool allowPrivateNetwork, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(Uri uri, bool allowPrivateNetwork, CancellationToken cancellationToken = default);
}

public sealed class WebhookDestinationValidator : IWebhookDestinationValidator
{
    public async Task ValidateAsync(Uri uri, bool allowPrivateNetwork, CancellationToken cancellationToken = default) =>
        _ = await ResolveAndValidateAsync(uri, allowPrivateNetwork, cancellationToken);

    public async Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        Uri uri,
        bool allowPrivateNetwork,
        CancellationToken cancellationToken = default)
    {
        ValidateUri(uri);
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken); }
            catch (SocketException ex)
            {
                throw new PalOpsApiException(422, "WEBHOOK_DESTINATION_UNRESOLVED", "Webhook 主机无法解析。", uri.DnsSafeHost, null, ex);
            }
        }
        if (addresses.Length == 0)
            throw new PalOpsApiException(422, "WEBHOOK_DESTINATION_UNRESOLVED", "Webhook 主机未解析到任何地址。", uri.DnsSafeHost);

        foreach (var address in addresses)
        {
            if (IsAlwaysBlocked(address))
                throw new PalOpsApiException(422, "WEBHOOK_DESTINATION_BLOCKED", "Webhook 地址解析到了禁止访问的网络。", address.ToString());
            if (!allowPrivateNetwork && IsPrivate(address))
                throw new PalOpsApiException(422, "WEBHOOK_PRIVATE_NETWORK_NOT_ALLOWED", "Webhook 私有网络目标需要 Owner 明确启用。", address.ToString());
        }
        return addresses;
    }

    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new PalOpsApiException(422, "WEBHOOK_URL_INVALID", "Webhook URL 必须是绝对 HTTP 或 HTTPS 地址。");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new PalOpsApiException(422, "WEBHOOK_URL_INVALID", "Webhook URL 不能包含用户名或密码。");
        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new PalOpsApiException(422, "WEBHOOK_URL_INVALID", "Webhook URL 不能包含片段标识。");
        if (uri.AbsoluteUri.Length > 2048)
            throw new PalOpsApiException(422, "WEBHOOK_URL_TOO_LONG", "Webhook URL 不能超过 2048 个字符。");
    }

    private static bool IsAlwaysBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] >= 224
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }
        var bytes4 = address.GetAddressBytes();
        return bytes4[0] == 10
            || (bytes4[0] == 172 && bytes4[1] is >= 16 and <= 31)
            || (bytes4[0] == 192 && bytes4[1] == 168)
            || (bytes4[0] == 100 && bytes4[1] is >= 64 and <= 127);
    }
}
