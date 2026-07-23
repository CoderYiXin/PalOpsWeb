using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PalOps.Web.Events;

namespace PalOps.Web.Versioning;

public sealed class PlatformVersionService(
    IApplicationVersionProvider versionProvider,
    IPalOpsReleaseClient releaseClient,
    IPalOpsEventPublisher eventPublisher,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<PlatformVersionService> logger) : IPlatformVersionService
{
    private const string CacheKey = "platform-version-status";
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _stateLock = new();
    private DateTimeOffset _lastManualCheckAt = DateTimeOffset.MinValue;
    private string? _lastPublishedTag;

    public async Task<PlatformVersionStatus> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && TryGetCached(out var cached)) return cached;

        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetCached(out cached)) return cached;
            var now = timeProvider.GetUtcNow();
            if (forceRefresh && TryUseManualCooldown(now, out cached)) return cached;

            var application = versionProvider.Get();
            PalOpsReleaseInfo? release = null;
            string? message = null;
            try
            {
                release = await releaseClient.GetLatestStableAsync(cancellationToken);
                if (release is null)
                    message = "GitHub 未返回可用的 PalOps Web 稳定版。";
                else if (release.Source == ReleaseSource.GitHubWeb)
                    message = "GitHub API 不可用，已通过 GitHub 网页备用通道完成检查。";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                message = "GitHub 版本检查超时，API 与网页备用通道均未返回结果。";
                logger.LogDebug("PalOps Web GitHub Release check timed out.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
            {
                message = "GitHub 远端版本检查失败：" + ex.Message;
                logger.LogDebug(ex, "Unable to read the latest PalOps Web GitHub Release.");
            }

            var latestVersion = release is null
                ? string.Empty
                : ApplicationVersionProvider.NormalizeDisplayVersion(release.TagName);
            var comparisonStatus = Classify(application.CurrentVersion, latestVersion, release is not null);
            if (comparisonStatus == PlatformVersionComparisonStatuses.Invalid)
                message = Join(message, "The current or latest version format cannot be compared.");

            var status = new PlatformVersionStatus(
                application.Application,
                application.CurrentVersion,
                latestVersion,
                comparisonStatus,
                comparisonStatus == PlatformVersionComparisonStatuses.UpdateAvailable,
                release?.TagName ?? string.Empty,
                release?.Name ?? string.Empty,
                release?.HtmlUrl ?? string.Empty,
                release?.Body ?? string.Empty,
                release?.PublishedAt,
                now,
                application.Runtime,
                application.OperatingSystem,
                application.Architecture,
                message);

            cache.Set(
                CacheKey,
                status,
                comparisonStatus is PlatformVersionComparisonStatuses.Unavailable or PlatformVersionComparisonStatuses.Invalid
                    ? FailureCacheDuration
                    : SuccessCacheDuration);

            if (status.UpdateAvailable && release is not null && ShouldPublish(release.TagName))
            {
                await eventPublisher.PublishAsync(PalOpsEvent.Create(
                    "palops.update-available",
                    "warning",
                    metadata: new Dictionary<string, object?>
                    {
                        ["currentVersion"] = status.CurrentVersion,
                        ["latestVersion"] = status.LatestVersion,
                        ["releaseTag"] = status.ReleaseTag,
                        ["releaseName"] = status.ReleaseName,
                        ["releaseUrl"] = status.ReleaseUrl
                    }), cancellationToken);
            }
            return status;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private bool TryGetCached(out PlatformVersionStatus status) =>
        cache.TryGetValue(CacheKey, out status!);

    private bool TryUseManualCooldown(DateTimeOffset now, out PlatformVersionStatus status)
    {
        lock (_stateLock)
        {
            if (now - _lastManualCheckAt < ManualCooldown && TryGetCached(out status))
            {
                status = status with
                {
                    Message = Join(status.Message, "Manual checks have a 10-second cooldown; the latest cached result was returned.")
                };
                return true;
            }
            _lastManualCheckAt = now;
            status = default!;
            return false;
        }
    }

    private bool ShouldPublish(string releaseTag)
    {
        lock (_stateLock)
        {
            if (string.Equals(_lastPublishedTag, releaseTag, StringComparison.OrdinalIgnoreCase)) return false;
            _lastPublishedTag = releaseTag;
            return true;
        }
    }

    private static string Classify(string currentText, string latestText, bool releaseAvailable)
    {
        if (!releaseAvailable) return PlatformVersionComparisonStatuses.Unavailable;
        if (!SemanticVersionValue.TryParse(currentText, out var current)
            || !SemanticVersionValue.TryParse(latestText, out var latest))
            return PlatformVersionComparisonStatuses.Invalid;
        var comparison = latest.CompareTo(current);
        if (comparison > 0) return PlatformVersionComparisonStatuses.UpdateAvailable;
        if (comparison < 0) return PlatformVersionComparisonStatuses.Ahead;
        return PlatformVersionComparisonStatuses.UpToDate;
    }

    private static string? Join(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return string.IsNullOrWhiteSpace(second) ? null : second.Trim();
        if (string.IsNullOrWhiteSpace(second)) return first.Trim();
        return first.Trim() + " " + second.Trim();
    }
}
