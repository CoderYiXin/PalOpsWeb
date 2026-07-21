namespace PalOps.Web.Versioning;

public static class PlatformVersionComparisonStatuses
{
    public const string UpToDate = "up-to-date";
    public const string UpdateAvailable = "update-available";
    public const string Ahead = "ahead";
    public const string Invalid = "invalid";
    public const string Unavailable = "unavailable";
}

public sealed record PlatformVersionStatus(
    string Application,
    string CurrentVersion,
    string LatestVersion,
    string ComparisonStatus,
    bool UpdateAvailable,
    string ReleaseTag,
    string ReleaseName,
    string ReleaseUrl,
    string ReleaseBody,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CheckedAt,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    string? Message);

public interface IPlatformVersionService
{
    Task<PlatformVersionStatus> CheckAsync(
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}
