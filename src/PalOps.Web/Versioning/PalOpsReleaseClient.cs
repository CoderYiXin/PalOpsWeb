using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalOps.Web.Versioning;

public sealed record PalOpsReleaseInfo(
    string TagName,
    string Name,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    string Body);

public interface IPalOpsReleaseClient
{
    Task<PalOpsReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default);
}

public sealed class PalOpsReleaseClient(
    HttpClient httpClient,
    IApplicationVersionProvider versionProvider) : IPalOpsReleaseClient
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PalOpsReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "repos/CoderYiXin/PalOpsWeb/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"PalOps-Web/{versionProvider.Get().ProductHeaderVersion}");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidDataException("GitHub Release response exceeded the 2 MiB limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new BoundedReadStream(stream, MaximumResponseBytes);
        var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(bounded, JsonOptions, cancellationToken);
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName)) return null;
        return new(
            release.TagName.Trim(),
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName.Trim() : release.Name.Trim(),
            NormalizeReleaseUrl(release.HtmlUrl),
            release.PublishedAt,
            Limit(release.Body ?? string.Empty, 4000));
    }

    private static string NormalizeReleaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (!uri.AbsolutePath.StartsWith("/CoderYiXin/PalOpsWeb/", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

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
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = maximumBytes - _read;
            if (remaining <= 0)
            {
                var probe = new byte[1];
                if (await inner.ReadAsync(probe, cancellationToken) > 0)
                    throw new InvalidDataException("GitHub Release response exceeded the 2 MiB limit.");
                return 0;
            }
            var count = await inner.ReadAsync(
                buffer[..(int)Math.Min(buffer.Length, remaining)],
                cancellationToken);
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
