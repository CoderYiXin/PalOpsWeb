using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Map;

public static class MapFetcher
{
    public static async Task<int> RunAsync(
        RepositoryPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly(
            "root", "layer", "force", "max-concurrency", "timeout-seconds", "retries");
        var selectedLayer = arguments.GetOptional("layer") ?? "all";
        var force = arguments.HasFlag("force");
        var maximumConcurrency = arguments.GetInt("max-concurrency", 8, 1, 32);
        var timeoutSeconds = arguments.GetInt("timeout-seconds", 30, 5, 300);
        var retries = arguments.GetInt("retries", 3, 0, 10);

        var sources = await MapSourceLoader.LoadAsync(paths, cancellationToken);
        var summaries = MapVerifier.ValidateSources(sources);
        var selected = selectedLayer.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? sources.Layers.ToArray()
            : sources.Layers
                .Where(layer => layer.Id.Equals(selectedLayer, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (selected.Length == 0)
            throw ToolExitException.Usage(
                $"未知地图图层“{selectedLayer}”。可用值：all、palpagos、world-tree。");

        Directory.CreateDirectory(paths.FrontendTilesPath);
        foreach (var layer in selected)
            RemoveStalePartFiles(paths.FrontendTilesPath, layer);

        var work = selected
            .SelectMany(layer => MapDatasetBuilder.EnumerateExpectedTiles(layer)
                .Select(tile => new TileDownload(layer, tile)))
            .ToArray();
        var errors = new ConcurrentBag<string>();
        var downloaded = 0;
        var skipped = 0;
        var completed = 0;

        using var handler = new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PalOps.Tooling/1.0 offline-map-fetcher");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));

        await Parallel.ForEachAsync(
            work,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maximumConcurrency,
                CancellationToken = cancellationToken
            },
            async (download, token) =>
            {
                try
                {
                    var result = await FetchOneAsync(
                        client,
                        paths.FrontendTilesPath,
                        download,
                        force,
                        timeoutSeconds,
                        retries,
                        token);
                    if (result == TileFetchResult.Downloaded)
                        Interlocked.Increment(ref downloaded);
                    else
                        Interlocked.Increment(ref skipped);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errors.Add(
                        $"{download.Layer.Id} z{download.Tile.Zoom}/{download.Tile.X}/{download.Tile.Y}: {exception.Message}");
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    if (done == work.Length || done % 25 == 0)
                        Console.WriteLine($"Map fetch progress: {done}/{work.Length}");
                }
            });

        var dataset = await MapDatasetBuilder.BuildAsync(
            paths,
            sources,
            summaries,
            paths.FrontendTilesPath,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await MapDatasetWriter.WriteAsync(paths.FrontendTilesPath, dataset, cancellationToken);

        if (!errors.IsEmpty)
        {
            var ordered = errors.Order(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
            throw ToolExitException.Verification(
                $"地图下载失败 {errors.Count}/{work.Length}：" + Environment.NewLine +
                string.Join(Environment.NewLine, ordered.Select(error => "- " + error)));
        }

        var selectedStatuses = dataset.Layers
            .Where(layer => selected.Any(item => item.Id == layer.Id))
            .Where(layer => layer.Status != "ready")
            .ToArray();
        if (selectedStatuses.Length > 0)
            throw ToolExitException.Verification(
                "下载结束但图层仍不完整：" +
                string.Join(", ", selectedStatuses.Select(layer => $"{layer.Id}={layer.Status} ({layer.Reason})")));

        Console.WriteLine(
            $"PASS map fetch: downloaded={downloaded}, skipped={skipped}, dataset={Path.Combine(paths.FrontendTilesPath, "dataset.json")}");
        return 0;
    }

    private static async Task<TileFetchResult> FetchOneAsync(
        HttpClient client,
        string tilesRoot,
        TileDownload download,
        bool force,
        int timeoutSeconds,
        int retries,
        CancellationToken cancellationToken)
    {
        var destination = MapDatasetBuilder.GetFilePath(tilesRoot, download.Layer, download.Tile);
        if (!force && File.Exists(destination))
        {
            var existing = await WebpValidator.InspectAsync(destination, cancellationToken);
            if (existing.IsValid)
                return TileFetchResult.Skipped;
        }

        var directory = Path.GetDirectoryName(destination)
                        ?? throw new InvalidOperationException($"无法确定瓦片目录：{destination}");
        Directory.CreateDirectory(directory);
        var url = ExpandRemoteUrl(download.Layer.RemoteUrlTemplate, download.Tile);
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporary = destination + ".part-" + Guid.NewGuid().ToString("N");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var response = await client.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var transient = response.StatusCode is HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests ||
                        status >= 500;
                    throw new TileDownloadException(
                        $"HTTP {status} {response.ReasonPhrase}", transient);
                }

                var expectedLength = response.Content.Headers.ContentLength;
                await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                await using (var output = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, timeout.Token);
                    await output.FlushAsync(timeout.Token);
                    output.Flush(flushToDisk: true);
                }

                var actualLength = new FileInfo(temporary).Length;
                if (expectedLength.HasValue && expectedLength.Value != actualLength)
                    throw new TileDownloadException(
                        $"Content-Length={expectedLength.Value}，实际={actualLength}", transient: true);
                var inspection = await WebpValidator.InspectAsync(temporary, cancellationToken);
                if (!inspection.IsValid)
                    throw new TileDownloadException($"WebP 无效：{inspection.Reason}", transient: true);

                File.Move(temporary, destination, overwrite: true);
                return TileFetchResult.Downloaded;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException($"请求超时（{timeoutSeconds} 秒）。");
            }
            catch (TileDownloadException exception)
            {
                lastError = exception;
                if (!exception.Transient)
                    throw;
            }
            catch (HttpRequestException exception)
            {
                lastError = exception;
            }
            catch (IOException exception)
            {
                lastError = exception;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            if (attempt < retries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(5000, 500 * (1 << Math.Min(attempt, 3))));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(lastError?.Message ?? "未知下载错误。", lastError);
    }

    private static string ExpandRemoteUrl(string template, TileCoordinate tile) =>
        template
            .Replace("{z}", tile.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", tile.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", tile.X.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static void RemoveStalePartFiles(string tilesRoot, MapSourceLayer layer)
    {
        var layerDirectory = Path.GetFullPath(Path.Combine(tilesRoot, layer.LocalDirectory));
        var root = Path.GetFullPath(tilesRoot);
        if (!layerDirectory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw ToolExitException.Verification($"拒绝清理瓦片根目录之外的路径：{layerDirectory}");
        if (!Directory.Exists(layerDirectory))
            return;
        foreach (var file in Directory.EnumerateFiles(layerDirectory, "*.part-*", SearchOption.AllDirectories))
            File.Delete(file);
    }

    private sealed record TileDownload(MapSourceLayer Layer, TileCoordinate Tile);

    private sealed class TileDownloadException : Exception
    {
        public TileDownloadException(string message, bool transient)
            : base(message)
        {
            Transient = transient;
        }

        public bool Transient { get; }
    }

    private enum TileFetchResult
    {
        Downloaded,
        Skipped
    }
}
