using System.Text.Json.Serialization;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.PluginManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginPackageKind
{
    PalDefender,
    UE4SS,
    ServerMod,
    DllPlugin,
    ScriptPlugin
}

public sealed record PluginDependencyManifest(
    string PackageId,
    string MinimumVersion,
    bool Optional);

public sealed record PluginPackageManifest(
    int SchemaVersion,
    string Id,
    string Name,
    PluginPackageKind Kind,
    string Version,
    string InstallDirectory,
    IReadOnlyList<string> EntryPaths,
    IReadOnlyList<PluginDependencyManifest> Dependencies,
    IReadOnlyList<string> CompatibleGameVersions,
    string Repository,
    string? Description);

public sealed record ManagedPluginRegistration(
    string Id,
    string Name,
    PluginPackageKind Kind,
    string Version,
    string InstallDirectory,
    IReadOnlyList<string> EntryPaths,
    IReadOnlyList<string> InstalledFiles,
    IReadOnlyList<PluginDependencyManifest> Dependencies,
    IReadOnlyList<string> CompatibleGameVersions,
    string Repository,
    string ArchiveSha256,
    bool Enabled,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record PluginReleaseStatus(
    string PackageId,
    string CurrentVersion,
    string LatestVersion,
    bool LatestAvailable,
    bool ComparisonAvailable,
    bool UpdateAvailable,
    string ReleaseUrl,
    string ReleaseName,
    string ReleaseBody,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CheckedAt,
    string? Message);

public sealed record PluginDependencyStatus(
    string PackageId,
    string MinimumVersion,
    bool Optional,
    bool Installed,
    bool Enabled,
    string InstalledVersion,
    bool VersionSatisfied,
    string Status);

public sealed record PluginInventoryItem(
    string Id,
    string Name,
    PluginPackageKind Kind,
    string Version,
    string LatestVersion,
    bool UpdateAvailable,
    string ReleaseUrl,
    string? ReleaseMessage,
    bool Enabled,
    bool Managed,
    bool CanToggle,
    string InstallDirectory,
    string PrimaryPath,
    string Sha256,
    long SizeBytes,
    int FileCount,
    DateTimeOffset? LastWriteAt,
    string Compatibility,
    string CompatibilityMessage,
    IReadOnlyList<PluginDependencyStatus> Dependencies,
    IReadOnlyList<string> Warnings);

public sealed record PluginBackupRecord(
    string BackupId,
    string PackageId,
    string PackageName,
    string Reason,
    string ArchiveFileName,
    string ArchiveSha256,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool Restored,
    DateTimeOffset? RestoredAt,
    string? RestoredBy,
    ManagedPluginRegistration? PreviousRegistration,
    IReadOnlyList<string> AffectedFiles);

public sealed record PluginOperationRecord(
    string OperationId,
    string Operation,
    string PackageId,
    string PackageName,
    string Outcome,
    string Operator,
    string RemoteIp,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Summary,
    string? Error);

public sealed class PluginManagementState
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, ManagedPluginRegistration> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PluginReleaseStatus> Releases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PluginBackupRecord> Backups { get; set; } = [];
    public List<PluginOperationRecord> History { get; set; } = [];
}

public sealed record PluginManagementDashboard(
    string ServerRoot,
    string GameVersion,
    bool ServerRunning,
    DateTimeOffset ScannedAt,
    IReadOnlyList<PluginInventoryItem> Packages,
    IReadOnlyList<PluginBackupRecord> Backups,
    IReadOnlyList<PluginOperationRecord> RecentOperations,
    IReadOnlyList<string> Warnings);

public sealed record PluginToggleRequest(bool Enabled);

public sealed record PluginOperationResult(
    string OperationId,
    string PackageId,
    string Operation,
    string Outcome,
    string Message,
    PluginManagementDashboard Dashboard);

public sealed record GitHubPluginRelease(
    string Repository,
    string TagName,
    string Name,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    string Body);

public sealed class PluginManagementException(
    int statusCode,
    string code,
    string message,
    string? detail = null,
    string? suggestedAction = null,
    Exception? innerException = null)
    : PalOpsApiException(statusCode, code, message, detail, suggestedAction, innerException);
