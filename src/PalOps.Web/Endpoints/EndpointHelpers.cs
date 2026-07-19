using System.Text.RegularExpressions;

namespace PalOps.Web.Endpoints;

internal static partial class EndpointHelpers
{
    public static string RemoteIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static string ValidatePlayerIdentifier(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!PlayerIdentifierPattern().IsMatch(normalized))
        {
            throw new ArgumentException("玩家标识只能包含字母、数字、下划线、连字符、冒号和点。", nameof(value));
        }

        return normalized;
    }

    public static string LimitForAudit(string value, int maximum = 200)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerIdentifierPattern();
}
