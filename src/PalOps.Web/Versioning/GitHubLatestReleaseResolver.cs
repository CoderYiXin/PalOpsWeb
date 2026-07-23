using System.Net;
using System.Net.Http.Headers;

namespace PalOps.Web.Versioning;

public static class ReleaseSource
{
    public const string GitHubApi = "github-api";
    public const string GitHubWeb = "github-web";
}

public sealed record GitHubLatestReleaseLink(string TagName, string HtmlUrl);

public static class GitHubLatestReleaseResolver
{
    public static async Task<GitHubLatestReleaseLink> ResolveAsync(
        HttpClient httpClient,
        string owner,
        string repository,
        string productHeaderVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var latestUri = new Uri($"https://github.com/{owner}/{repository}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, latestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.UserAgent.ParseAdd($"PalOps-Web/{productHeaderVersion}");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var releaseUri = ResolveReleaseUri(response, latestUri);
        if (releaseUri is null)
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidDataException("GitHub latest-release page did not expose a release tag URL.");
        }

        return ParseReleaseUri(releaseUri, owner, repository);
    }

    private static Uri? ResolveReleaseUri(HttpResponseMessage response, Uri requestUri)
    {
        if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            return location.IsAbsoluteUri ? location : new Uri(requestUri, location);

        if (response.IsSuccessStatusCode)
            return response.RequestMessage?.RequestUri;

        return null;
    }

    private static GitHubLatestReleaseLink ParseReleaseUri(Uri releaseUri, string owner, string repository)
    {
        if (!releaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub latest-release redirect returned an untrusted URL.");

        var prefix = $"/{owner}/{repository}/releases/tag/";
        if (!releaseUri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub latest-release redirect did not contain a release tag.");

        var encodedTag = releaseUri.AbsolutePath[prefix.Length..].Trim('/');
        var tag = Uri.UnescapeDataString(encodedTag).Trim();
        if (string.IsNullOrWhiteSpace(tag) || tag.Contains('/'))
            throw new InvalidDataException("GitHub latest-release redirect contained an invalid release tag.");

        var normalizedUrl = releaseUri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
        return new GitHubLatestReleaseLink(tag, normalizedUrl);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}

public sealed class GitHubReleaseLookupException : HttpRequestException
{
    public GitHubReleaseLookupException(string repository, Exception apiException, Exception webException)
        : base(
            $"GitHub API 与网页备用通道均失败：API {Describe(apiException)}；备用通道 {Describe(webException)}。",
            new AggregateException(apiException, webException))
    {
        Repository = repository;
    }

    public string Repository { get; }

    private static string Describe(Exception exception)
    {
        if (exception is OperationCanceledException) return "请求超时";
        if (exception is HttpRequestException { StatusCode: { } statusCode })
            return $"HTTP {(int)statusCode} {statusCode}";

        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 160 ? message : message[..160];
    }
}
