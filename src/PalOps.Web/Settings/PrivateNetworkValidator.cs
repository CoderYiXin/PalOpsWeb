using System.Net;
using System.Net.Sockets;

namespace PalOps.Web.Settings;

public sealed record EndpointValidationResult(bool IsValid, string Message);

public interface IPrivateNetworkValidator
{
    Task<EndpointValidationResult> ValidateHttpEndpointAsync(string value, bool allowPublic, CancellationToken cancellationToken = default);
    Task<EndpointValidationResult> ValidateHostAsync(string value, bool allowPublic, CancellationToken cancellationToken = default);
}

public sealed class PrivateNetworkValidator : IPrivateNetworkValidator
{
    public async Task<EndpointValidationResult> ValidateHttpEndpointAsync(string value, bool allowPublic, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return new(false, "地址格式无效。");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return new(false, "仅允许 HTTP 或 HTTPS 地址。");
        }

        if (string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0)
        {
            return new(false, "地址不能包含用户信息，且必须包含主机名。");
        }

        return await ValidateHostAsync(uri.Host, allowPublic, cancellationToken);
    }

    public async Task<EndpointValidationResult> ValidateHostAsync(string value, bool allowPublic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            return new(false, "主机名无效。");
        }

        if (allowPublic)
        {
            return new(true, "允许公开地址。");
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) || IsSingleLabelHost(value))
        {
            return new(true, "局域网主机名。");
        }

        if (IPAddress.TryParse(value, out var parsed))
        {
            return IsPrivate(parsed) ? new(true, "私网地址。") : new(false, "禁止连接公网地址。");
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(value, cancellationToken);
            if (addresses.Length == 0)
            {
                return new(false, "主机名没有解析结果。");
            }

            return addresses.All(IsPrivate)
                ? new(true, "主机名解析为私网地址。")
                : new(false, "主机名包含公网解析结果，已拒绝。");
        }
        catch (SocketException)
        {
            return new(false, "无法解析主机名。");
        }
    }

    public static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static bool IsSingleLabelHost(string value)
        => !value.Contains('.') && !value.Contains(':')
            && value.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_');
}
