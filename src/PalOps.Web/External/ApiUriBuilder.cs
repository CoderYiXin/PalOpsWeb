namespace PalOps.Web.External;

internal static class ApiUriBuilder
{
    public static Uri Palworld(string baseUrl, string relative)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (normalized.EndsWith("/v1/api", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized + "/" + relative.TrimStart('/'), UriKind.Absolute);
        }

        return new Uri(normalized + "/v1/api/" + relative.TrimStart('/'), UriKind.Absolute);
    }

    public static Uri PalDefender(string baseUrl, string relative)
    {
        var normalized = baseUrl.TrimEnd('/');
        var endpoint = relative.TrimStart('/');
        if (endpoint.Equals("version", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith("/v1/pdapi/version", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized, UriKind.Absolute);
        }
        if (normalized.EndsWith("/v1/pdapi", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized + "/" + endpoint, UriKind.Absolute);
        }

        return new Uri(normalized + "/v1/pdapi/" + endpoint, UriKind.Absolute);
    }
}
