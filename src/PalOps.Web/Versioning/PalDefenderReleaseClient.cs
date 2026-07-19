using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalOps.Web.Versioning;

public sealed record PalDefenderReleaseInfo(
    string TagName,
    string Name,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    string Body);

public interface IPalDefenderReleaseClient
{
    Task<PalDefenderReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default);
}

public sealed class PalDefenderReleaseClient(HttpClient httpClient) : IPalDefenderReleaseClient
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PalDefenderReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "repos/Ultimeit/PalDefender/releases?per_page=30");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!request.Headers.UserAgent.Any())
        {
            var informationalVersion = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.1.0";
            request.Headers.UserAgent.ParseAdd($"PalOps-Web/{SanitizeProductVersion(informationalVersion)}");
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidDataException("GitHub Releases 响应超过 2 MiB 限制。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new BoundedReadStream(stream, MaximumResponseBytes);
        var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(bounded, JsonOptions, cancellationToken) ?? [];
        var stable = releases.FirstOrDefault(release => !release.Draft && !release.Prerelease);
        if (stable is null || string.IsNullOrWhiteSpace(stable.TagName)) return null;
        return new(
            stable.TagName.Trim(),
            string.IsNullOrWhiteSpace(stable.Name) ? stable.TagName.Trim() : stable.Name.Trim(),
            stable.HtmlUrl?.Trim() ?? string.Empty,
            stable.PublishedAt,
            Limit(stable.Body ?? string.Empty, 4000));
    }

    private static string SanitizeProductVersion(string value)
    {
        var normalized = value.Split('+', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "1.1.0"
            : new string(normalized.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-').ToArray());
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
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = maximumBytes - _read;
            if (remaining <= 0)
            {
                var probe = new byte[1];
                if (await inner.ReadAsync(probe, cancellationToken) > 0) throw new InvalidDataException("GitHub Releases 响应超过 2 MiB 限制。");
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
