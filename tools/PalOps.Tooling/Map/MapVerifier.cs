using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Map;

public static class MapVerifier
{
    private static readonly string[] RequiredLayerIds = ["palpagos", "world-tree"];

    public static async Task<int> RunAsync(
        RepositoryPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root", "sources-only", "allow-missing", "tiles-root", "require-layers");
        var sourcesOnly = arguments.HasFlag("sources-only");
        var allowMissing = arguments.HasFlag("allow-missing");
        var tilesRoot = arguments.GetOptionalPath("tiles-root") ?? paths.FrontendTilesPath;
        var requiredLayers = ParseRequiredLayers(arguments.GetOptional("require-layers"));

        var manifest = await MapSourceLoader.LoadAsync(paths, cancellationToken);
        var summaries = ValidateSources(manifest);
        foreach (var summary in summaries)
            Console.WriteLine(
                $"PASS map source {summary.LayerId}: tiles={summary.ExpectedTiles}, samples={summary.SampleCount}, maxError={summary.MaximumError:G6}");

        if (sourcesOnly)
        {
            Console.WriteLine(
                $"PASS map source definitions: game={manifest.GameVersion}, dataset={manifest.DatasetVersion}, layers={summaries.Count}");
            return 0;
        }

        var datasetPath = Path.Combine(tilesRoot, "dataset.json");
        if (!File.Exists(datasetPath))
        {
            var message = $"地图数据集缺失：{datasetPath}。运行 .\\scripts\\fetch-map-tiles.ps1 -Layer all 获取素材。";
            if (!allowMissing || requiredLayers.Count > 0)
                throw ToolExitException.Verification(message);
            Console.WriteLine($"DEGRADED map dataset: status=missing; {message}");
            return 0;
        }

        var dataset = await JsonFile.ReadAsync<MapDatasetManifest>(datasetPath, cancellationToken);
        var actual = await MapDatasetBuilder.BuildAsync(
            paths,
            manifest,
            summaries,
            tilesRoot,
            dataset.FetchedAt,
            cancellationToken);
        var completelyMissing = allowMissing &&
                                requiredLayers.Count == 0 &&
                                actual.Layers.All(layer =>
                                    layer.ActualTileCount == 0 &&
                                    layer.InvalidTiles.Count == 0 &&
                                    layer.ExtraTiles.Count == 0);
        var problems = ValidateDataset(
            manifest,
            dataset,
            actual,
            compareObservedState: !completelyMissing);
        if (problems.Count > 0)
            throw ToolExitException.Verification(
                "地图数据集校验失败：" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "- " + problem)));

        foreach (var layer in actual.Layers)
            Console.WriteLine(
                $"Map layer {layer.Id}: status={layer.Status}, tiles={layer.ActualTileCount}/{layer.ExpectedTileCount}" +
                (string.IsNullOrEmpty(layer.Reason) ? string.Empty : $", reason={layer.Reason}"));

        if (completelyMissing)
        {
            Console.WriteLine(
                $"DEGRADED map dataset: metadata=valid, complete=0/{actual.Layers.Count}, root={tilesRoot}; " +
                "run .\\scripts\\fetch-map-tiles.ps1 -Layer all to fetch local tiles.");
            return 0;
        }

        var invalid = actual.Layers.Where(layer => layer.Status == "invalid").ToArray();
        if (invalid.Length > 0)
            throw ToolExitException.Verification(
                "地图数据集包含无效图层：" +
                string.Join(", ", invalid.Select(layer => $"{layer.Id} ({layer.Reason})")));

        var strictLayerIds = requiredLayers.Count > 0
            ? requiredLayers
            : allowMissing
                ? new HashSet<string>(StringComparer.Ordinal)
                : RequiredLayerIds.ToHashSet(StringComparer.Ordinal);
        var incomplete = actual.Layers
            .Where(layer => strictLayerIds.Contains(layer.Id) && layer.Status != "ready")
            .ToArray();
        if (incomplete.Length > 0)
            throw ToolExitException.Verification(
                "必需地图图层不完整：" +
                string.Join(", ", incomplete.Select(layer => $"{layer.Id}={layer.Status}")));

        Console.WriteLine(
            $"PASS map dataset: complete={actual.CompleteLayerIds.Count}/{actual.Layers.Count}, root={tilesRoot}");
        return 0;
    }

    public static IReadOnlyList<MapSourceValidationSummary> ValidateSources(MapSourceManifest manifest)
    {
        var problems = new List<string>();
        if (manifest.SchemaVersion != 1)
            problems.Add($"schemaVersion 必须为 1，实际为 {manifest.SchemaVersion}。");
        if (!string.Equals(manifest.GameVersion, "1.0", StringComparison.Ordinal))
            problems.Add($"gameVersion 必须为 1.0，实际为“{manifest.GameVersion}”。");
        if (string.IsNullOrWhiteSpace(manifest.DatasetVersion))
            problems.Add("datasetVersion 不能为空。");
        if (!string.Equals(manifest.DefaultLayerId, "palpagos", StringComparison.Ordinal))
            problems.Add("defaultLayerId 必须为 palpagos。");

        var layers = manifest.Layers ?? [];
        var duplicateId = layers.GroupBy(layer => layer.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            problems.Add($"图层 ID 重复：{duplicateId.Key}。");

        var actualIds = layers.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in RequiredLayerIds)
        {
            if (!actualIds.Contains(requiredId))
                problems.Add($"缺少必需图层：{requiredId}。");
        }
        foreach (var unexpected in actualIds.Except(RequiredLayerIds, StringComparer.Ordinal))
            problems.Add($"存在未批准的图层：{unexpected}。");

        var summaries = new List<MapSourceValidationSummary>();
        foreach (var layer in layers)
            summaries.Add(ValidateLayer(layer, problems));

        if (problems.Count > 0)
            throw ToolExitException.Verification(
                "地图来源定义校验失败：" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "- " + problem)));

        return summaries;
    }

    private static MapSourceValidationSummary ValidateLayer(MapSourceLayer layer, List<string> problems)
    {
        var prefix = string.IsNullOrWhiteSpace(layer.Id) ? "<empty>" : layer.Id;
        if (string.IsNullOrWhiteSpace(layer.DisplayName))
            problems.Add($"{prefix}: displayName 不能为空。");
        if (!string.Equals(layer.LocalDirectory, layer.Id, StringComparison.Ordinal))
            problems.Add($"{prefix}: localDirectory 必须等于图层 ID。");
        if (layer.LocalDirectory.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            layer.LocalDirectory.Contains("..", StringComparison.Ordinal))
            problems.Add($"{prefix}: localDirectory 不安全。");

        var expectedLocalTemplate = $"/map/tiles/{layer.LocalDirectory}/{{z}}/{{x}}/{{y}}.webp";
        if (!string.Equals(layer.LocalUrlTemplate, expectedLocalTemplate, StringComparison.Ordinal))
            problems.Add($"{prefix}: localUrlTemplate 必须为 {expectedLocalTemplate}。");
        if (!Uri.TryCreate(layer.RemoteUrlTemplate, UriKind.Absolute, out var remoteUri) ||
            remoteUri.Scheme != Uri.UriSchemeHttps)
            problems.Add($"{prefix}: remoteUrlTemplate 必须是具体的 HTTPS URL。");
        if (!HasSingleToken(layer.RemoteUrlTemplate, "{z}") ||
            !HasSingleToken(layer.RemoteUrlTemplate, "{y}") ||
            !HasSingleToken(layer.RemoteUrlTemplate, "{x}"))
            problems.Add($"{prefix}: remoteUrlTemplate 必须各包含一次 {{z}}/{{y}}/{{x}}。");
        if (!string.Equals(layer.RemoteAxisOrder, "zyx", StringComparison.Ordinal))
            problems.Add($"{prefix}: remoteAxisOrder 必须为 zyx。");
        if (!string.Equals(layer.Format, "webp", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{prefix}: format 必须为 webp。");
        if (layer.TileSize != 512)
            problems.Add($"{prefix}: tileSize 必须为 512。");
        if (layer.MinimumZoom != 0 || layer.MaximumZoom != 4)
            problems.Add($"{prefix}: 原生缩放必须为 0..4。");

        ValidateBounds(layer.WorldBounds, prefix + ".worldBounds", problems);
        ValidateBounds(layer.MapBounds, prefix + ".mapBounds", problems);

        var worldToMap = layer.WorldToMap.ToTransform();
        var mapToPixel = layer.MapToPixel.ToTransform();
        ValidateTransform(worldToMap, prefix + ".worldToMap", problems);
        ValidateTransform(mapToPixel, prefix + ".mapToPixel", problems);

        if (Math.Abs(mapToPixel.B) > 1e-12 || Math.Abs(mapToPixel.D) > 1e-12)
            problems.Add($"{prefix}: mapToPixel 必须可表示为 Leaflet 的独立 X/Y 变换。");
        if (!NearlyEqual(layer.LeafletTransformation.A, mapToPixel.A) ||
            !NearlyEqual(layer.LeafletTransformation.B, mapToPixel.C) ||
            !NearlyEqual(layer.LeafletTransformation.C, mapToPixel.E) ||
            !NearlyEqual(layer.LeafletTransformation.D, mapToPixel.F))
            problems.Add($"{prefix}: leafletTransformation 与 mapToPixel 不一致。");

        var expectedTiles = ValidateRanges(layer, prefix, problems);
        var maximumError = ValidateSamples(layer, worldToMap, mapToPixel, prefix, problems);

        if (string.IsNullOrWhiteSpace(layer.Source.Name) ||
            !Uri.TryCreate(layer.Source.PageUrl, UriKind.Absolute, out var pageUri) ||
            pageUri.Scheme != Uri.UriSchemeHttps)
            problems.Add($"{prefix}: source 名称和 HTTPS pageUrl 必须完整。");
        if (string.IsNullOrWhiteSpace(layer.Source.MapUpdatedAt) ||
            string.IsNullOrWhiteSpace(layer.Source.VerifiedAt) ||
            string.IsNullOrWhiteSpace(layer.Source.RightsNotice))
            problems.Add($"{prefix}: source 更新时间、核验日期和权利说明不能为空。");
        if (layer.Source.RedistributionAllowed)
            problems.Add($"{prefix}: redistributionAllowed 必须为 false，不能替素材来源方授权。");

        return new MapSourceValidationSummary(
            prefix,
            expectedTiles,
            layer.CalibrationSamples.Count,
            maximumError);
    }

    private static int ValidateRanges(MapSourceLayer layer, string prefix, List<string> problems)
    {
        var ranges = layer.ZoomRanges.OrderBy(range => range.Zoom).ToArray();
        var expectedZooms = Enumerable.Range(layer.MinimumZoom, layer.MaximumZoom - layer.MinimumZoom + 1).ToArray();
        if (!ranges.Select(range => range.Zoom).SequenceEqual(expectedZooms))
        {
            problems.Add($"{prefix}: zoomRanges 必须恰好覆盖 0..4。");
            return 0;
        }

        var total = 0;
        foreach (var range in ranges)
        {
            var maximum = (1 << range.Zoom) - 1;
            if (range.MinimumX != 0 || range.MinimumY != 0 ||
                range.MaximumX != maximum || range.MaximumY != maximum)
                problems.Add($"{prefix}: z{range.Zoom} 范围必须为 x/y 0..{maximum}。");
            try
            {
                total = checked(total + range.Count);
            }
            catch (OverflowException)
            {
                problems.Add($"{prefix}: z{range.Zoom} 瓦片数量溢出。");
            }
        }

        if (total != 341)
            problems.Add($"{prefix}: 预期瓦片总数必须为 341，实际为 {total}。");
        return total;
    }

    private static double ValidateSamples(
        MapSourceLayer layer,
        AffineTransform worldToMap,
        AffineTransform mapToPixel,
        string prefix,
        List<string> problems)
    {
        var samples = layer.CalibrationSamples ?? [];
        if (samples.Count < 3)
        {
            problems.Add($"{prefix}: 至少需要三个校准样本。");
            return double.PositiveInfinity;
        }
        if (!ContainsNonCollinearSamples(samples))
            problems.Add($"{prefix}: 校准样本不能全部共线。");

        AffineTransform inverseWorldToMap = default;
        AffineTransform inverseMapToPixel = default;
        try
        {
            inverseWorldToMap = worldToMap.Inverse();
            inverseMapToPixel = mapToPixel.Inverse();
        }
        catch (InvalidOperationException)
        {
            return double.PositiveInfinity;
        }

        var maximumError = 0d;
        foreach (var sample in samples)
        {
            if (string.IsNullOrWhiteSpace(sample.Name) ||
                !double.IsFinite(sample.Tolerance) ||
                sample.Tolerance <= 0 ||
                sample.Tolerance > 1)
            {
                problems.Add($"{prefix}: 校准样本名称或容差无效。");
                continue;
            }

            var world = new MapPoint(sample.WorldX, sample.WorldY);
            var expectedMap = new MapPoint(sample.MapX, sample.MapY);
            var expectedPixel = new MapPoint(sample.PixelX, sample.PixelY);
            var errors = new[]
            {
                worldToMap.Apply(world).DistanceTo(expectedMap),
                mapToPixel.Apply(expectedMap).DistanceTo(expectedPixel),
                inverseWorldToMap.Apply(expectedMap).DistanceTo(world),
                inverseMapToPixel.Apply(expectedPixel).DistanceTo(expectedMap),
                inverseWorldToMap.Apply(worldToMap.Apply(world)).DistanceTo(world),
                inverseMapToPixel.Apply(mapToPixel.Apply(expectedMap)).DistanceTo(expectedMap)
            };
            var error = errors.Max();
            maximumError = Math.Max(maximumError, error);
            if (!double.IsFinite(error) || error > sample.Tolerance)
                problems.Add($"{prefix}: 校准样本 {sample.Name} 误差 {error:G17} 超过容差 {sample.Tolerance:G17}。");
        }

        return maximumError;
    }

    private static bool ContainsNonCollinearSamples(IReadOnlyList<CalibrationSampleDefinition> samples)
    {
        for (var first = 0; first < samples.Count - 2; first++)
        for (var second = first + 1; second < samples.Count - 1; second++)
        for (var third = second + 1; third < samples.Count; third++)
        {
            var a = samples[first];
            var b = samples[second];
            var c = samples[third];
            var twiceArea =
                (b.WorldX - a.WorldX) * (c.WorldY - a.WorldY) -
                (b.WorldY - a.WorldY) * (c.WorldX - a.WorldX);
            if (double.IsFinite(twiceArea) && Math.Abs(twiceArea) > 1e-6)
                return true;
        }

        return false;
    }

    private static void ValidateBounds(CoordinateBounds bounds, string label, List<string> problems)
    {
        if (!double.IsFinite(bounds.MinimumX) || !double.IsFinite(bounds.MaximumX) ||
            !double.IsFinite(bounds.MinimumY) || !double.IsFinite(bounds.MaximumY) ||
            bounds.MinimumX >= bounds.MaximumX ||
            bounds.MinimumY >= bounds.MaximumY)
            problems.Add($"{label}: 边界必须是有限且递增的数值。");
    }

    private static void ValidateTransform(AffineTransform transform, string label, List<string> problems)
    {
        if (!transform.IsFinite)
            problems.Add($"{label}: 系数必须全部为有限数。");
        if (!double.IsFinite(transform.Determinant) || Math.Abs(transform.Determinant) < 1e-12)
            problems.Add($"{label}: 矩阵不可逆。");
    }

    private static bool HasSingleToken(string value, string token) =>
        value.IndexOf(token, StringComparison.Ordinal) >= 0 &&
        value.IndexOf(token, value.IndexOf(token, StringComparison.Ordinal) + token.Length, StringComparison.Ordinal) < 0;

    private static bool NearlyEqual(double first, double second) =>
        double.IsFinite(first) && double.IsFinite(second) && Math.Abs(first - second) <= 1e-12;

    private static IReadOnlyList<string> ValidateDataset(
        MapSourceManifest sources,
        MapDatasetManifest declared,
        MapDatasetManifest actual,
        bool compareObservedState)
    {
        var problems = new List<string>();
        if (declared.SchemaVersion != sources.SchemaVersion)
            problems.Add($"dataset schemaVersion={declared.SchemaVersion}，来源定义={sources.SchemaVersion}。");
        if (!string.Equals(declared.GameVersion, sources.GameVersion, StringComparison.Ordinal))
            problems.Add($"dataset gameVersion={declared.GameVersion}，来源定义={sources.GameVersion}。");
        if (!string.Equals(declared.DatasetVersion, sources.DatasetVersion, StringComparison.Ordinal))
            problems.Add($"datasetVersion 已过期：{declared.DatasetVersion} != {sources.DatasetVersion}。");
        if (!string.Equals(declared.DefaultLayerId, sources.DefaultLayerId, StringComparison.Ordinal))
            problems.Add($"defaultLayerId 不一致：{declared.DefaultLayerId} != {sources.DefaultLayerId}。");
        if (!string.Equals(
                declared.SourceDefinitionSha256, actual.SourceDefinitionSha256,
                StringComparison.OrdinalIgnoreCase))
            problems.Add("dataset.json 的来源定义 SHA-256 已过期。");
        if (declared.FetchedAt is null && declared.Layers.Any(layer => layer.Status == "ready"))
            problems.Add("完整数据集必须包含 fetchedAt。");

        var duplicate = declared.Layers
            .GroupBy(layer => layer.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            problems.Add($"dataset 图层 ID 重复：{duplicate.Key}。");
            return problems;
        }

        var sourceById = sources.Layers.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        var declaredById = declared.Layers.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        var actualById = actual.Layers.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        foreach (var source in sources.Layers)
        {
            if (!declaredById.TryGetValue(source.Id, out var layer))
            {
                problems.Add($"dataset 缺少图层：{source.Id}。");
                continue;
            }
            var inspected = actualById[source.Id];
            ValidateLayerMetadata(source, layer, problems);
            ValidateDeclaredTileManifest(source, layer, problems);
            if (compareObservedState)
            {
                if (!string.Equals(layer.Status, inspected.Status, StringComparison.Ordinal))
                    problems.Add($"{source.Id}: 声明状态 {layer.Status}，实际状态 {inspected.Status}。");
                if (!string.Equals(layer.Reason, inspected.Reason, StringComparison.Ordinal))
                    problems.Add($"{source.Id}: 状态原因与实际扫描结果不一致。");
                if (layer.ExpectedTileCount != inspected.ExpectedTileCount ||
                    layer.ActualTileCount != inspected.ActualTileCount ||
                    layer.MissingTileCount != inspected.MissingTileCount)
                    problems.Add($"{source.Id}: 瓦片计数与实际扫描结果不一致。");
                if (!layer.InvalidTiles.SequenceEqual(inspected.InvalidTiles, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{source.Id}: invalidTiles 与实际扫描结果不一致。");
                if (!layer.ExtraTiles.SequenceEqual(inspected.ExtraTiles, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{source.Id}: extraTiles 与实际扫描结果不一致。");
                ValidateTileManifest(source.Id, layer.Tiles, inspected.Tiles, problems);
            }
        }

        foreach (var unexpected in declaredById.Keys.Except(sourceById.Keys, StringComparer.Ordinal))
            problems.Add($"dataset 包含未知图层：{unexpected}。");

        if (compareObservedState)
        {
            var declaredComplete = declared.CompleteLayerIds.Order(StringComparer.Ordinal).ToArray();
            var actualComplete = actual.CompleteLayerIds.Order(StringComparer.Ordinal).ToArray();
            if (!declaredComplete.SequenceEqual(actualComplete, StringComparer.Ordinal))
                problems.Add(
                    $"completeLayerIds 不一致：声明 [{string.Join(",", declaredComplete)}]，实际 [{string.Join(",", actualComplete)}]。");
        }

        var declaredReady = declared.Layers
            .Where(layer => layer.Status == "ready")
            .Select(layer => layer.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var declaredCompleteIds = declared.CompleteLayerIds.Order(StringComparer.Ordinal).ToArray();
        if (!declaredCompleteIds.SequenceEqual(declaredReady, StringComparer.Ordinal))
            problems.Add(
                $"completeLayerIds 与声明状态不一致：完整 [{string.Join(",", declaredCompleteIds)}]，ready [{string.Join(",", declaredReady)}]。");

        return problems;
    }

    private static void ValidateDeclaredTileManifest(
        MapSourceLayer source,
        MapDatasetLayer layer,
        List<string> problems)
    {
        var layerId = source.Id;
        var expectedPaths = MapDatasetBuilder.EnumerateExpectedTiles(source)
            .Select(tile => MapDatasetBuilder.GetRelativePath(source, tile))
            .ToHashSet(StringComparer.Ordinal);
        if (layer.ExpectedTileCount != expectedPaths.Count ||
            layer.ActualTileCount != layer.Tiles.Count ||
            layer.MissingTileCount != expectedPaths.Count - layer.Tiles.Count)
            problems.Add($"{layerId}: dataset 声明计数彼此不一致。");

        var validStatuses = new[] { "ready", "partial", "missing", "invalid" };
        if (!validStatuses.Contains(layer.Status, StringComparer.Ordinal))
            problems.Add($"{layerId}: dataset 状态无效：{layer.Status}。");
        if (layer.Status == "ready" &&
            (layer.Tiles.Count != expectedPaths.Count ||
             layer.MissingTileCount != 0 ||
             layer.InvalidTiles.Count != 0 ||
             layer.ExtraTiles.Count != 0))
            problems.Add($"{layerId}: ready 图层必须声明完整且无无效/多余瓦片。");

        var duplicate = layer.Tiles
            .GroupBy(tile => tile.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            problems.Add($"{layerId}: dataset 瓦片路径重复 {duplicate.Key}。");

        foreach (var tile in layer.Tiles)
        {
            var expectedPath = $"{layerId}/{tile.Zoom}/{tile.X}/{tile.Y}.webp";
            if (!string.Equals(tile.RelativePath, expectedPath, StringComparison.Ordinal) ||
                !expectedPaths.Contains(tile.RelativePath) ||
                tile.RelativePath.StartsWith("/", StringComparison.Ordinal) ||
                tile.RelativePath.Contains("..", StringComparison.Ordinal) ||
                tile.RelativePath.Contains('\\'))
                problems.Add($"{layerId}: 瓦片相对路径不安全或与坐标不一致 {tile.RelativePath}。");
            if (tile.Length < 12)
                problems.Add($"{layerId}: 瓦片长度无效 {tile.RelativePath}。");
            if (tile.Sha256.Length != 64 ||
                tile.Sha256.Any(character => !Uri.IsHexDigit(character)))
                problems.Add($"{layerId}: 瓦片 SHA-256 无效 {tile.RelativePath}。");
        }
    }

    private static void ValidateLayerMetadata(
        MapSourceLayer source,
        MapDatasetLayer layer,
        List<string> problems)
    {
        var prefix = source.Id;
        if (layer.LocalUrlTemplate.Contains("://", StringComparison.Ordinal) ||
            !string.Equals(layer.LocalUrlTemplate, source.LocalUrlTemplate, StringComparison.Ordinal))
            problems.Add($"{prefix}: dataset localUrlTemplate 必须是经过验证的本地模板。");
        if (!string.Equals(layer.DisplayName, source.DisplayName, StringComparison.Ordinal) ||
            !string.Equals(layer.LocalDirectory, source.LocalDirectory, StringComparison.Ordinal) ||
            !string.Equals(layer.Format, source.Format, StringComparison.Ordinal) ||
            layer.TileSize != source.TileSize ||
            layer.MinimumZoom != source.MinimumZoom ||
            layer.MaximumZoom != source.MaximumZoom)
            problems.Add($"{prefix}: dataset 基础图层元数据与来源定义不一致。");
        if (!BoundsEqual(layer.WorldBounds, source.WorldBounds) ||
            !BoundsEqual(layer.MapBounds, source.MapBounds) ||
            !AffineEqual(layer.WorldToMap, source.WorldToMap) ||
            !AffineEqual(layer.MapToPixel, source.MapToPixel) ||
            !LeafletEqual(layer.LeafletTransformation, source.LeafletTransformation))
            problems.Add($"{prefix}: dataset 坐标边界或变换与来源定义不一致。");
        if (layer.ZoomRanges.Count != source.ZoomRanges.Count ||
            !layer.ZoomRanges.Zip(source.ZoomRanges).All(pair =>
                pair.First.Zoom == pair.Second.Zoom &&
                pair.First.MinimumX == pair.Second.MinimumX &&
                pair.First.MaximumX == pair.Second.MaximumX &&
                pair.First.MinimumY == pair.Second.MinimumY &&
                pair.First.MaximumY == pair.Second.MaximumY))
            problems.Add($"{prefix}: dataset zoomRanges 与来源定义不一致。");
        if (!string.Equals(layer.CalibrationStatus, "valid", StringComparison.Ordinal) ||
            layer.MaximumCalibrationError > 0.01 ||
            !double.IsFinite(layer.MaximumCalibrationError))
            problems.Add($"{prefix}: dataset 校准状态无效。");
        if (layer.Source.RedistributionAllowed ||
            string.IsNullOrWhiteSpace(layer.Source.RightsNotice) ||
            !string.Equals(layer.Source.PageUrl, source.Source.PageUrl, StringComparison.Ordinal))
            problems.Add($"{prefix}: dataset 来源或权利说明无效。");
    }

    private static void ValidateTileManifest(
        string layerId,
        IReadOnlyList<MapDatasetTile> declared,
        IReadOnlyList<MapDatasetTile> actual,
        List<string> problems)
    {
        if (declared.Count != actual.Count)
        {
            problems.Add($"{layerId}: dataset tiles 数量 {declared.Count}，实际有效瓦片 {actual.Count}。");
            return;
        }
        var declaredByPath = declared.ToDictionary(tile => tile.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var tile in actual)
        {
            if (!declaredByPath.TryGetValue(tile.RelativePath, out var expected))
            {
                problems.Add($"{layerId}: dataset 缺少瓦片记录 {tile.RelativePath}。");
                continue;
            }
            if (expected.Zoom != tile.Zoom || expected.X != tile.X || expected.Y != tile.Y ||
                expected.Length != tile.Length ||
                !string.Equals(expected.Sha256, tile.Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{layerId}: 瓦片清单不匹配 {tile.RelativePath}。");
            if (expected.RelativePath.StartsWith("/", StringComparison.Ordinal) ||
                expected.RelativePath.Contains("..", StringComparison.Ordinal) ||
                expected.RelativePath.Contains('\\'))
                problems.Add($"{layerId}: 瓦片相对路径不安全 {expected.RelativePath}。");
        }
    }

    private static bool BoundsEqual(CoordinateBounds first, CoordinateBounds second) =>
        NearlyEqual(first.MinimumX, second.MinimumX) &&
        NearlyEqual(first.MaximumX, second.MaximumX) &&
        NearlyEqual(first.MinimumY, second.MinimumY) &&
        NearlyEqual(first.MaximumY, second.MaximumY);

    private static bool AffineEqual(AffineDefinition first, AffineDefinition second) =>
        NearlyEqual(first.A, second.A) &&
        NearlyEqual(first.B, second.B) &&
        NearlyEqual(first.C, second.C) &&
        NearlyEqual(first.D, second.D) &&
        NearlyEqual(first.E, second.E) &&
        NearlyEqual(first.F, second.F);

    private static bool LeafletEqual(
        LeafletTransformationDefinition first,
        LeafletTransformationDefinition second) =>
        NearlyEqual(first.A, second.A) &&
        NearlyEqual(first.B, second.B) &&
        NearlyEqual(first.C, second.C) &&
        NearlyEqual(first.D, second.D);

    private static IReadOnlySet<string> ParseRequiredLayers(string? value)
    {
        if (value is null)
            return new HashSet<string>(StringComparer.Ordinal);
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 0)
            throw ToolExitException.Usage("--require-layers 至少需要一个图层 ID。");
        var result = values.ToHashSet(StringComparer.Ordinal);
        var unknown = result.FirstOrDefault(id => !RequiredLayerIds.Contains(id, StringComparer.Ordinal));
        if (unknown is not null)
            throw ToolExitException.Usage($"--require-layers 包含未知图层：{unknown}。");
        return result;
    }
}

public sealed record MapSourceValidationSummary(
    string LayerId,
    int ExpectedTiles,
    int SampleCount,
    double MaximumError);
