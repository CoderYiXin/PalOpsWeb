using PalOps.Web.Infrastructure;

namespace PalOps.Web.PalworldConfiguration;

public static class PalworldConfigurationSeverity
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Information = "information";
}

public sealed record PalworldConfigurationDiagnostic(
    string Code,
    string Severity,
    string Path,
    string Message);

public sealed record PalworldConfigurationFieldMetadata(
    string Key,
    string ValueType,
    string Group,
    string ChineseName,
    string EnglishName,
    string JapaneseName,
    string ChineseDescription,
    string EnglishDescription,
    string JapaneseDescription,
    string? DefaultValue = null,
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<string>? Options = null,
    bool RequiresRestart = true,
    bool Sensitive = false,
    bool Advanced = false,
    bool PerformanceRisk = false,
    bool EnforceRange = false);

public sealed record PalworldConfigurationMetadataResponse(
    string DocumentationUrl,
    string StartupArgumentsDocumentationUrl,
    IReadOnlyList<PalworldConfigurationFieldMetadata> Fields,
    IReadOnlyList<PalworldConfigurationFieldMetadata> LaunchArguments);

public sealed record PalworldConfigurationSnapshot(
    string ConfigurationPath,
    bool ConfigurationExists,
    string Source,
    string RawContent,
    string Sha256,
    long SizeBytes,
    DateTimeOffset? ModifiedAt,
    IReadOnlyDictionary<string, string> Settings,
    string LaunchArguments,
    bool RuntimeConfigurationConfirmed,
    DateTimeOffset RuntimeConfigurationUpdatedAt,
    string WorldOptionPath,
    bool WorldOptionExists,
    long? WorldOptionSizeBytes,
    DateTimeOffset? WorldOptionModifiedAt,
    IReadOnlyList<PalworldConfigurationDiagnostic> Diagnostics,
    PalworldConfigurationMetadataResponse Metadata);

public sealed record PalworldConfigurationPreviewRequest(
    string RawContent,
    string LaunchArguments);

public sealed record PalworldConfigurationSaveRequest(
    string RawContent,
    string LaunchArguments,
    string? ExpectedSha256,
    DateTimeOffset? ExpectedRuntimeConfigurationUpdatedAt,
    bool ConfirmWarnings = false);

public sealed record PalworldConfigurationPathRequest(string Path);

public sealed record PalworldConfigurationChange(
    string Source,
    string Key,
    string? Before,
    string? After,
    bool RequiresRestart,
    bool Sensitive);

public sealed record PalworldConfigurationValidationResult(
    bool Valid,
    string NormalizedContent,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<PalworldConfigurationDiagnostic> Diagnostics);

public sealed record PalworldConfigurationPreview(
    bool Valid,
    string NormalizedContent,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<PalworldConfigurationDiagnostic> Diagnostics,
    IReadOnlyList<PalworldConfigurationChange> Changes,
    bool RestartRequired,
    bool HasWarnings);

public sealed record PalworldConfigurationSaveResult(
    PalworldConfigurationSnapshot Configuration,
    string BackupPath,
    bool RestartRequested,
    string? RestartOperationId);

public sealed class PalworldConfigurationException(
    int statusCode,
    string code,
    string message,
    string? detail = null,
    string? suggestedAction = null,
    Exception? innerException = null)
    : PalOpsApiException(statusCode, code, message, detail, suggestedAction, innerException);
