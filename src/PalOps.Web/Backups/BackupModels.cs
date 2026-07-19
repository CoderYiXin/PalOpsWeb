namespace PalOps.Web.Backups;

public sealed record BackupRecord(
    string Id,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string Sha256,
    string Status,
    string Note,
    string WorldId,
    int FileCount,
    bool ExecuteSaveFirst,
    DateTimeOffset? VerifiedAt,
    string? Error);

public sealed record BackupManifest(
    string Id,
    string WorldId,
    DateTimeOffset CreatedAt,
    string SourceWorldDirectory,
    IReadOnlyList<BackupManifestFile> Files);

public sealed record BackupManifestFile(string RelativePath, long SizeBytes, string Sha256);
