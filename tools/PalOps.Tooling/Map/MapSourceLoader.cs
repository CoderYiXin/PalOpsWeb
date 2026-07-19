using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Map;

public static class MapSourceLoader
{
    public static Task<MapSourceManifest> LoadAsync(
        RepositoryPaths paths,
        CancellationToken cancellationToken) =>
        JsonFile.ReadAsync<MapSourceManifest>(paths.MapSourcesPath, cancellationToken);
}
