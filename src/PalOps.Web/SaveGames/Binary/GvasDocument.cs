namespace PalOps.Web.SaveGames.Binary;

public sealed record GvasEngineVersion(
    ushort Major,
    ushort Minor,
    ushort Patch,
    uint Changelist,
    string Branch);

public sealed record GvasCustomVersion(Guid Key, int Version);

public sealed record GvasDocument(
    int SaveGameVersion,
    int PackageFileVersionUe4,
    int PackageFileVersionUe5,
    GvasEngineVersion EngineVersion,
    int CustomVersionFormat,
    IReadOnlyList<GvasCustomVersion> CustomVersions,
    string SaveGameClassName,
    IReadOnlyDictionary<string, GvasProperty> Properties,
    ReadOnlyMemory<byte> Trailer);

public sealed record GvasPropertyBagResult(
    IReadOnlyDictionary<string, GvasProperty> Properties,
    int ConsumedBytes,
    ReadOnlyMemory<byte> Remaining);
