namespace PalOps.Tooling.Map;

public sealed class MapDatasetManifest
{
    public int SchemaVersion { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public string DatasetVersion { get; init; } = string.Empty;
    public string DefaultLayerId { get; init; } = string.Empty;
    public string SourceDefinitionSha256 { get; init; } = string.Empty;
    public DateTimeOffset? FetchedAt { get; init; }
    public IReadOnlyList<string> CompleteLayerIds { get; init; } = [];
    public IReadOnlyList<MapDatasetLayer> Layers { get; init; } = [];
}

public sealed class MapDatasetLayer
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LocalDirectory { get; init; } = string.Empty;
    public string LocalUrlTemplate { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public int TileSize { get; init; }
    public int MinimumZoom { get; init; }
    public int MaximumZoom { get; init; }
    public CoordinateBounds WorldBounds { get; init; } = new();
    public CoordinateBounds MapBounds { get; init; } = new();
    public AffineDefinition WorldToMap { get; init; } = new();
    public AffineDefinition MapToPixel { get; init; } = new();
    public LeafletTransformationDefinition LeafletTransformation { get; init; } = new();
    public IReadOnlyList<TileRangeDefinition> ZoomRanges { get; init; } = [];
    public IReadOnlyList<CalibrationSampleDefinition> CalibrationSamples { get; init; } = [];
    public MapSourceAttribution Source { get; init; } = new();
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string CalibrationStatus { get; init; } = string.Empty;
    public double MaximumCalibrationError { get; init; }
    public int ExpectedTileCount { get; init; }
    public int ActualTileCount { get; init; }
    public int MissingTileCount { get; init; }
    public IReadOnlyList<string> InvalidTiles { get; init; } = [];
    public IReadOnlyList<string> ExtraTiles { get; init; } = [];
    public IReadOnlyList<MapDatasetTile> Tiles { get; init; } = [];
}

public sealed class MapDatasetTile
{
    public int Zoom { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
