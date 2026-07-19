namespace PalOps.Tooling.Map;

public sealed class MapSourceManifest
{
    public int SchemaVersion { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public string DatasetVersion { get; init; } = string.Empty;
    public string DefaultLayerId { get; init; } = string.Empty;
    public IReadOnlyList<MapSourceLayer> Layers { get; init; } = [];
}

public sealed class MapSourceLayer
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LocalDirectory { get; init; } = string.Empty;
    public string LocalUrlTemplate { get; init; } = string.Empty;
    public string RemoteUrlTemplate { get; init; } = string.Empty;
    public string RemoteAxisOrder { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public int TileSize { get; init; }
    public int MinimumZoom { get; init; }
    public int MaximumZoom { get; init; }
    public CoordinateBounds WorldBounds { get; init; } = new();
    public CoordinateBounds MapBounds { get; init; } = new();
    public AffineDefinition WorldToMap { get; init; } = new();
    public AffineDefinition MapToPixel { get; init; } = new();
    public LeafletTransformationDefinition SourceLeafletTransformation { get; init; } = new();
    public LeafletTransformationDefinition LeafletTransformation { get; init; } = new();
    public IReadOnlyList<TileRangeDefinition> ZoomRanges { get; init; } = [];
    public IReadOnlyList<CalibrationSampleDefinition> CalibrationSamples { get; init; } = [];
    public MapSourceAttribution Source { get; init; } = new();
}

public sealed class CoordinateBounds
{
    public double MinimumX { get; init; }
    public double MaximumX { get; init; }
    public double MinimumY { get; init; }
    public double MaximumY { get; init; }
}

public sealed class AffineDefinition
{
    public double A { get; init; }
    public double B { get; init; }
    public double C { get; init; }
    public double D { get; init; }
    public double E { get; init; }
    public double F { get; init; }

    public AffineTransform ToTransform() => new(A, B, C, D, E, F);
}

public sealed class LeafletTransformationDefinition
{
    public double A { get; init; }
    public double B { get; init; }
    public double C { get; init; }
    public double D { get; init; }
}

public sealed class TileRangeDefinition
{
    public int Zoom { get; init; }
    public int MinimumX { get; init; }
    public int MaximumX { get; init; }
    public int MinimumY { get; init; }
    public int MaximumY { get; init; }

    public int Count => checked((MaximumX - MinimumX + 1) * (MaximumY - MinimumY + 1));
}

public sealed class CalibrationSampleDefinition
{
    public string Name { get; init; } = string.Empty;
    public double WorldX { get; init; }
    public double WorldY { get; init; }
    public double MapX { get; init; }
    public double MapY { get; init; }
    public double PixelX { get; init; }
    public double PixelY { get; init; }
    public double Tolerance { get; init; }
}

public sealed class MapSourceAttribution
{
    public string Name { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public string MapUpdatedAt { get; init; } = string.Empty;
    public string VerifiedAt { get; init; } = string.Empty;
    public string RightsNotice { get; init; } = string.Empty;
    public bool RedistributionAllowed { get; init; }
}
