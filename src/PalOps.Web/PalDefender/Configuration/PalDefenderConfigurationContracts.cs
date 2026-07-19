namespace PalOps.Web.PalDefender.Configuration;

public sealed record PalDefenderConfigFileSummary(
    string RelativePath,
    string FileName,
    string Kind,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Sha256,
    bool CanDelete,
    string ActivationHint);

public sealed record PalDefenderConfigFileContent(
    PalDefenderConfigFileSummary File,
    string Content);

public sealed record PalDefenderConfigDiagnostic(
    string Path,
    string Severity,
    string Message);

public sealed record PalDefenderConfigValidation(
    bool Valid,
    string Kind,
    string NormalizedContent,
    string ActivationHint,
    IReadOnlyList<PalDefenderConfigDiagnostic> Diagnostics);

public sealed record PalDefenderConfigWriteRequest(
    string Content,
    string? ExpectedSha256);

public sealed record PalDefenderConfigValidateRequest(
    string RelativePath,
    string Content);

public sealed record PalDefenderConfigFieldMetadata(
    string Key,
    string JsonType,
    string Group,
    string ChineseName,
    string EnglishName,
    string JapaneseName,
    string ChineseDescription,
    string EnglishDescription,
    string JapaneseDescription,
    bool Deprecated = false,
    string? DefaultJson = null);

public sealed record PalDefenderConfigMetadata(
    string Kind,
    string DocumentationUrl,
    IReadOnlyList<PalDefenderConfigFieldMetadata> Fields);

public sealed record PalDefenderConfigGenerateRequest(
    string Kind,
    string? Name = null);

public sealed record PalDefenderGeneratedConfig(
    string RelativePath,
    string Kind,
    string Content);
