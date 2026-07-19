using System.Security.Cryptography;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Map;

public static class MapDatasetBuilder
{
    public static async Task<MapDatasetManifest> BuildAsync(
        RepositoryPaths paths,
        MapSourceManifest sources,
        IReadOnlyList<MapSourceValidationSummary> sourceSummaries,
        string tilesRoot,
        DateTimeOffset? fetchedAt,
        CancellationToken cancellationToken)
    {
        var summaryByLayer = sourceSummaries.ToDictionary(
            summary => summary.LayerId, StringComparer.Ordinal);
        var layers = new List<MapDatasetLayer>();
        foreach (var source in sources.Layers)
        {
            layers.Add(await InspectLayerAsync(
                source,
                summaryByLayer[source.Id],
                tilesRoot,
                cancellationToken));
        }

        await using var sourceStream = new FileStream(
            paths.MapSourcesPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sourceHash = await SHA256.HashDataAsync(sourceStream, cancellationToken);
        return new MapDatasetManifest
        {
            SchemaVersion = sources.SchemaVersion,
            GameVersion = sources.GameVersion,
            DatasetVersion = sources.DatasetVersion,
            DefaultLayerId = sources.DefaultLayerId,
            SourceDefinitionSha256 = Convert.ToHexString(sourceHash).ToLowerInvariant(),
            FetchedAt = fetchedAt,
            CompleteLayerIds = layers
                .Where(layer => layer.Status == "ready")
                .Select(layer => layer.Id)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Layers = layers
        };
    }

    public static IEnumerable<TileCoordinate> EnumerateExpectedTiles(MapSourceLayer layer)
    {
        foreach (var range in layer.ZoomRanges.OrderBy(range => range.Zoom))
        for (var x = range.MinimumX; x <= range.MaximumX; x++)
        for (var y = range.MinimumY; y <= range.MaximumY; y++)
            yield return new TileCoordinate(range.Zoom, x, y);
    }

    public static string GetRelativePath(MapSourceLayer layer, TileCoordinate tile) =>
        $"{layer.LocalDirectory}/{tile.Zoom}/{tile.X}/{tile.Y}.webp";

    public static string GetFilePath(
        string tilesRoot,
        MapSourceLayer layer,
        TileCoordinate tile) =>
        Path.Combine(
            tilesRoot,
            layer.LocalDirectory,
            tile.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tile.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tile.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".webp");

    private static async Task<MapDatasetLayer> InspectLayerAsync(
        MapSourceLayer source,
        MapSourceValidationSummary sourceSummary,
        string tilesRoot,
        CancellationToken cancellationToken)
    {
        var expected = EnumerateExpectedTiles(source).ToArray();
        var expectedPaths = expected.ToDictionary(
            tile => NormalizeRelative(GetRelativePath(source, tile)),
            tile => tile,
            StringComparer.OrdinalIgnoreCase);
        var validTiles = new List<MapDatasetTile>();
        var invalidTiles = new List<string>();

        foreach (var tile in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = GetFilePath(tilesRoot, source, tile);
            if (!File.Exists(filePath))
                continue;
            var inspection = await WebpValidator.InspectAsync(filePath, cancellationToken);
            var relative = NormalizeRelative(GetRelativePath(source, tile));
            if (!inspection.IsValid)
            {
                invalidTiles.Add($"{relative}: {inspection.Reason}");
                continue;
            }
            validTiles.Add(new MapDatasetTile
            {
                Zoom = tile.Zoom,
                X = tile.X,
                Y = tile.Y,
                RelativePath = relative,
                Length = inspection.Length,
                Sha256 = inspection.Sha256
            });
        }

        var layerDirectory = Path.Combine(tilesRoot, source.LocalDirectory);
        var extraTiles = new List<string>();
        if (Directory.Exists(layerDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(layerDirectory, "*.webp", SearchOption.AllDirectories))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(tilesRoot, file));
                if (!expectedPaths.ContainsKey(relative))
                    extraTiles.Add(relative);
            }
            foreach (var part in Directory.EnumerateFiles(layerDirectory, "*.part-*", SearchOption.AllDirectories))
                invalidTiles.Add($"{NormalizeRelative(Path.GetRelativePath(tilesRoot, part))}: 未完成的临时文件。");
        }

        validTiles.Sort((first, second) =>
        {
            var zoom = first.Zoom.CompareTo(second.Zoom);
            if (zoom != 0) return zoom;
            var x = first.X.CompareTo(second.X);
            return x != 0 ? x : first.Y.CompareTo(second.Y);
        });
        invalidTiles.Sort(StringComparer.OrdinalIgnoreCase);
        extraTiles.Sort(StringComparer.OrdinalIgnoreCase);

        var missing = expected.Length - validTiles.Count;
        string status;
        string reason;
        if (invalidTiles.Count > 0 || extraTiles.Count > 0)
        {
            status = "invalid";
            reason = $"存在 {invalidTiles.Count} 个无效文件和 {extraTiles.Count} 个多余瓦片。";
        }
        else if (missing == 0)
        {
            status = "ready";
            reason = string.Empty;
        }
        else if (validTiles.Count == 0)
        {
            status = "missing";
            reason = $"缺少全部 {expected.Length} 个瓦片。";
        }
        else
        {
            status = "partial";
            reason = $"缺少 {missing}/{expected.Length} 个瓦片。";
        }

        return new MapDatasetLayer
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            LocalDirectory = source.LocalDirectory,
            LocalUrlTemplate = source.LocalUrlTemplate,
            Format = source.Format,
            TileSize = source.TileSize,
            MinimumZoom = source.MinimumZoom,
            MaximumZoom = source.MaximumZoom,
            WorldBounds = source.WorldBounds,
            MapBounds = source.MapBounds,
            WorldToMap = source.WorldToMap,
            MapToPixel = source.MapToPixel,
            LeafletTransformation = source.LeafletTransformation,
            ZoomRanges = source.ZoomRanges,
            CalibrationSamples = source.CalibrationSamples,
            Source = new MapSourceAttribution
            {
                Name = source.Source.Name,
                PageUrl = source.Source.PageUrl,
                MapUpdatedAt = source.Source.MapUpdatedAt,
                VerifiedAt = source.Source.VerifiedAt,
                RightsNotice = source.Source.RightsNotice,
                RedistributionAllowed = source.Source.RedistributionAllowed
            },
            Status = status,
            Reason = reason,
            CalibrationStatus = "valid",
            MaximumCalibrationError = sourceSummary.MaximumError,
            ExpectedTileCount = expected.Length,
            ActualTileCount = validTiles.Count,
            MissingTileCount = missing,
            InvalidTiles = invalidTiles,
            ExtraTiles = extraTiles,
            Tiles = validTiles
        };
    }

    private static string NormalizeRelative(string value) =>
        value.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}

public readonly record struct TileCoordinate(int Zoom, int X, int Y);
