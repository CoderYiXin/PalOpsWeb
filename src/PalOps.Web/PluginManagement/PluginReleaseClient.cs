using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PalOps.Web.Versioning;

namespace PalOps.Web.PluginManagement;

public interface IPluginReleaseClient
{
    Task<GitHubPluginRelease?> GetLatestStableAsync(string repository, CancellationToken cancellationToken = default);
}

public sealed partial class PluginReleaseClient(
    HttpClient httpClient,
    IApplicationVersionProvider versionProvider) : IPluginReleaseClient
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubPluginRelease?> GetLatestStableAsync(string repository, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRepository(repository);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{normalized}/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"PalOps-Web/{versionProvider.Get().ProductHeaderVersion}");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidDataException("GitHub Releases 响应超过 2 MiB 限制。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new BoundedReadStream(stream, MaximumResponseBytes);
        var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(bounded, JsonOptions, cancellationToken) ?? new List<ReleaseDto>();
        var stable = releases.FirstOrDefault(static item => !item.Draft && !item.Prerelease && !string.IsNullOrWhiteSpace(item.TagName));
        if (stable is null) return null;
        return new(
            normalized,
            stable.TagName.Trim(),
            string.IsNullOrWhiteSpace(stable.Name) ? stable.TagName.Trim() : stable.Name.Trim(),
            NormalizeReleaseUrl(normalized, stable.HtmlUrl),
            stable.PublishedAt,
            Limit(stable.Body ?? string.Empty, 4000));
    }

    public static string NormalizeRepository(string repository)
    {
        var normalized = repository?.Trim() ?? string.Empty;
        if (!RepositoryPattern().IsMatch(normalized))
            throw new PluginManagementException(422, "PLUGIN_REPOSITORY_INVALID", "GitHub 仓库必须使用 owner/repository 格式。", repository);
        return normalized;
    }

    private static string NormalizeReleaseUrl(string repository, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/" + repository + "/", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        string? Body,
        bool Draft,
        bool Prerelease);

    private sealed class BoundedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = maximumBytes - _read;
            if (remaining <= 0)
            {
                var probe = new byte[1];
                if (await inner.ReadAsync(probe, cancellationToken) > 0)
                    throw new InvalidDataException("GitHub Releases 响应超过 2 MiB 限制。");
                return 0;
            }
            var count = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
            _read += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }
}
