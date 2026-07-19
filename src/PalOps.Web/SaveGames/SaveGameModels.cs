namespace PalOps.Web.SaveGames;

public sealed record SaveWorldCandidate(
    string WorldId,
    string WorldDirectory,
    string LevelPath,
    string PlayersPath,
    DateTimeOffset ModifiedAt,
    long SizeBytes,
    int PlayerSaveFiles,
    bool Configured);

public sealed record SaveFileFingerprint(long Length, DateTime LastWriteTimeUtc)
{
    public static SaveFileFingerprint Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("存档文件不存在。", path);
        return new SaveFileFingerprint(info.Length, info.LastWriteTimeUtc);
    }
}

public sealed record SaveSnapshotFile(
    string RelativePath,
    string FullPath,
    long SizeBytes,
    string Sha256,
    DateTimeOffset ModifiedAt);

public sealed record SaveSnapshotManifest(
    string SnapshotId,
    string WorldId,
    string SourceWorldDirectory,
    string SnapshotDirectory,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SaveSnapshotFile> Files,
    string LevelSha256,
    long TotalSizeBytes);

public enum SaveIndexState
{
    Idle,
    WaitingForStableFile,
    CopyingSnapshot,
    Decompressing,
    ReadingGvas,
    Players,
    Inventories,
    Pals,
    Guilds,
    Bases,
    MapObjects,
    WritingIndex,
    Completed,
    Failed,
    Cancelled
}

public sealed record SaveIndexStatus(
    SaveIndexState State,
    string Stage,
    int Progress,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CurrentSnapshotId,
    string? LastSuccessfulSnapshotId,
    DateTimeOffset? LastSuccessfulAt,
    bool UsingStaleSnapshot,
    string? Error,
    bool CanCancel)
{
    public static SaveIndexStatus Idle() => new(
        SaveIndexState.Idle,
        "idle",
        0,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        false);
}

public sealed record SaveIndexHistoryEntry(
    string SnapshotId,
    string WorldId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long DurationMilliseconds,
    bool Success,
    string LevelSha256,
    long LevelSizeBytes,
    int PlayerCount,
    int ItemCount,
    int PalCount,
    int GuildCount,
    int BaseCount,
    string? Error);
