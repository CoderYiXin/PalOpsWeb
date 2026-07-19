namespace PalOps.Web.SaveGames.Projection;

public sealed record BaseCampProjectionCandidate(
    string BaseId,
    string? DirectGuildId,
    IReadOnlyList<string> RelatedPlayerUids,
    WorldVector? DirectPosition,
    string DirectPositionSource,
    WorldVector? LinkedObjectPosition,
    int WorkerCount,
    int MapObjectCount,
    IReadOnlyDictionary<string, string> Evidence);

public readonly record struct WorldVector(double X, double Y, double Z);
