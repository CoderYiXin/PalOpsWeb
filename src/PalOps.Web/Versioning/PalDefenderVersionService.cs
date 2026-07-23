using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PalOps.Web.Events;
using PalOps.Web.External;
using PalOps.Web.Platform.Readiness;

namespace PalOps.Web.Versioning;

public sealed record PalDefenderVersionStatus(
    string CurrentVersion,
    string LatestVersion,
    bool CurrentAvailable,
    bool LatestAvailable,
    bool UpdateAvailable,
    bool ComparisonAvailable,
    string ReleaseUrl,
    string ReleaseName,
    string ReleaseBody,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CheckedAt,
    string? Message);

public interface IPalDefenderVersionService
{
    Task<PalDefenderVersionStatus> CheckAsync(bool forceRefresh, CancellationToken cancellationToken = default);
}

public sealed class PalDefenderVersionService(
    IPalDefenderReleaseClient releaseClient,
    IPalDefenderApiClient palDefenderClient,
    IPalOpsEventPublisher eventPublisher,
    IMemoryCache cache,
    ILogger<PalDefenderVersionService> logger,
    IOperationalReadinessGate readinessGate) : IPalDefenderVersionService
{
    private const string CacheKey = "paldefender-version-status";
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _stateLock = new();
    private DateTimeOffset _lastManualCheckAt = DateTimeOffset.MinValue;
    private string? _lastPublishedTag;

    public async Task<PalDefenderVersionStatus> CheckAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        var readiness = await readinessGate.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var canReadCurrentVersion = readiness.HasAny(OperationalCapability.PalDefender);
        var cacheKey = canReadCurrentVersion ? CacheKey + ":local-and-remote" : CacheKey + ":remote-only";

        if (!forceRefresh && cache.TryGetValue<PalDefenderVersionStatus>(cacheKey, out var cached) && cached is not null)
            return cached;

        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && cache.TryGetValue<PalDefenderVersionStatus>(cacheKey, out cached) && cached is not null)
                return cached;

            if (forceRefresh)
            {
                lock (_stateLock)
                {
                    if (DateTimeOffset.UtcNow - _lastManualCheckAt < TimeSpan.FromSeconds(10)
                        && cache.TryGetValue<PalDefenderVersionStatus>(cacheKey, out cached)
                        && cached is not null)
                    {
                        return cached with { Message = Join(cached.Message, "手动检查冷却时间为 10 秒，已返回最近结果。") };
                    }
                    _lastManualCheckAt = DateTimeOffset.UtcNow;
                }
            }

            string currentVersion = string.Empty;
            PalDefenderReleaseInfo? release = null;
            string? currentError = null;
            string? latestError = null;

            if (canReadCurrentVersion)
            {
                try
                {
                    currentVersion = NormalizeCurrentVersion(await palDefenderClient.GetVersionAsync(cancellationToken));
                    if (string.IsNullOrWhiteSpace(currentVersion)) currentError = "PalDefender 返回的当前版本无法识别。";
                }
                catch (Exception ex) when (ex is ExternalApiException or HttpRequestException or JsonException or InvalidDataException)
                {
                    currentError = ex.Message;
                    logger.LogDebug(ex, "Unable to read the current PalDefender version.");
                }
            }
            else
            {
                currentError = "PalDefender 尚未配置，已跳过本地版本读取；GitHub 远端版本仍会正常检查。";
            }

            try
            {
                release = await releaseClient.GetLatestStableAsync(cancellationToken);
                if (release is null)
                    latestError = "GitHub 未返回可用的 PalDefender 稳定版。";
                else if (release.Source == ReleaseSource.GitHubWeb)
                    latestError = "GitHub API 不可用，已通过 GitHub 网页备用通道完成远端版本检查。";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                latestError = "GitHub 版本检查超时，API 与网页备用通道均未返回结果。";
                logger.LogDebug("PalDefender GitHub Release check timed out.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or IOException)
            {
                latestError = "GitHub 远端版本检查失败：" + ex.Message;
                logger.LogDebug(ex, "Unable to read the latest PalDefender GitHub release.");
            }

            var currentAvailable = !string.IsNullOrWhiteSpace(currentVersion);
            var latestVersion = release?.TagName ?? string.Empty;
            var latestAvailable = !string.IsNullOrWhiteSpace(latestVersion);
            var currentParsed = SemanticVersionValue.TryParse(currentVersion, out var current);
            var latestParsed = SemanticVersionValue.TryParse(latestVersion, out var latest);
            var comparisonAvailable = currentParsed && latestParsed;
            var updateAvailable = comparisonAvailable && latest.CompareTo(current) > 0;
            var message = Join(currentError, latestError);
            if (currentAvailable && latestAvailable && !comparisonAvailable)
                message = Join(message, "当前版本或最新版本不是可比较的数字版本格式。");

            var status = new PalDefenderVersionStatus(
                currentVersion,
                latestVersion,
                currentAvailable,
                latestAvailable,
                updateAvailable,
                comparisonAvailable,
                release?.HtmlUrl ?? string.Empty,
                release?.Name ?? string.Empty,
                release?.Body ?? string.Empty,
                release?.PublishedAt,
                DateTimeOffset.UtcNow,
                message);

            cache.Set(
                cacheKey,
                status,
                latestAvailable ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(2));
            if (updateAvailable && release is not null && ShouldPublish(release.TagName))
            {
                await eventPublisher.PublishAsync(PalOpsEvent.Create(
                    "paldefender.update-available",
                    "warning",
                    metadata: new Dictionary<string, object?>
                    {
                        ["currentVersion"] = currentVersion,
                        ["latestVersion"] = release.TagName,
                        ["releaseUrl"] = release.HtmlUrl,
                        ["releaseName"] = release.Name
                    }), cancellationToken);
            }
            return status;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private bool ShouldPublish(string tag)
    {
        lock (_stateLock)
        {
            if (string.Equals(_lastPublishedTag, tag, StringComparison.OrdinalIgnoreCase)) return false;
            _lastPublishedTag = tag;
            return true;
        }
    }

    private static string NormalizeCurrentVersion(string raw) =>
        PalDefenderVersionPayloadParser.Parse(raw);

    private static string? Join(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return string.IsNullOrWhiteSpace(second) ? null : second.Trim();
        if (string.IsNullOrWhiteSpace(second)) return first.Trim();
        return first.Trim() + " " + second.Trim();
    }

}
