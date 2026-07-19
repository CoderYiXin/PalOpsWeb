using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Map;

public static class MapDatasetWriter
{
    public static Task WriteAsync(
        string tilesRoot,
        MapDatasetManifest dataset,
        CancellationToken cancellationToken) =>
        JsonFile.WriteAtomicAsync(
            Path.Combine(tilesRoot, "dataset.json"),
            dataset,
            cancellationToken);
}
