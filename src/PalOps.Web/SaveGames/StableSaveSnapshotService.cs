using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Settings;

namespace PalOps.Web.SaveGames;

public interface IStableSaveSnapshotService
{
    Task<SaveSnapshotManifest> CreateAsync(
        SaveWorldCandidate source,
        SaveGameSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class StableSaveSnapshotService : IStableSaveSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _incomingRoot;
    private readonly ISavePathGuard _pathGuard;
    private readonly ILogger<StableSaveSnapshotService> _logger;

    public StableSaveSnapshotService(
        IHostEnvironment environment,
        IOptions<AppRuntimeOptions> options,
        ISavePathGuard pathGuard,
        ILogger<StableSaveSnapshotService> logger)
    {
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        _incomingRoot = Path.Combine(dataDirectory, "snapshots", "incoming");
        Directory.CreateDirectory(_incomingRoot);
        _pathGuard = pathGuard;
        _logger = logger;
    }

    public async Task<SaveSnapshotManifest> CreateAsync(
        SaveWorldCandidate source,
        SaveGameSettings settings,
        CancellationToken cancellationToken = default)
    {
        var maximumBytes = checked((long)settings.MaximumFileSizeMb * 1024L * 1024L);
        await WaitForStableAsync(
            source.LevelPath,
            settings.StableChecks,
            settings.StableCheckIntervalSeconds,
            maximumBytes,
            cancellationToken);

        var snapshotId = $"{SaveClock.BeijingNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var snapshotDirectory = Path.Combine(_incomingRoot, snapshotId);
        Directory.CreateDirectory(snapshotDirectory);

        try
        {
            var files = new List<SaveSnapshotFile>();
            files.Add(await CopyFileAsync(source.WorldDirectory, source.LevelPath, snapshotDirectory, maximumBytes, cancellationToken));

            foreach (var playerFile in Directory.EnumerateFiles(source.PlayersPath, "*.sav", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(await CopyFileAsync(source.WorldDirectory, playerFile, snapshotDirectory, maximumBytes, cancellationToken));
            }

            var level = files.First(file => file.RelativePath.Equals("Level.sav", StringComparison.OrdinalIgnoreCase));
            var manifest = new SaveSnapshotManifest(
                snapshotId,
                source.WorldId,
                source.WorldDirectory,
                snapshotDirectory,
                SaveClock.BeijingNow(),
                files,
                level.Sha256,
                files.Sum(file => file.SizeBytes));

            var manifestPath = Path.Combine(snapshotDirectory, "manifest.json");
            await using var manifestStream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
            await manifestStream.FlushAsync(cancellationToken);
            return manifest;
        }
        catch
        {
            TryDeleteDirectory(snapshotDirectory);
            throw;
        }
    }

    private async Task WaitForStableAsync(
        string path,
        int checks,
        int intervalSeconds,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        SaveFileFingerprint? previous = null;
        var stableCount = 0;

        while (stableCount < checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = SaveFileFingerprint.Read(path);
            if (current.Length > maximumBytes)
                throw new InvalidOperationException($"存档文件 {current.Length:N0} 字节，超过配置上限 {maximumBytes:N0} 字节。");

            await EnsureReadableAsync(path, cancellationToken);
            if (previous == current)
                stableCount++;
            else
                stableCount = 1;

            previous = current;
            if (stableCount < checks)
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }
    }

    private static async Task EnsureReadableAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        var buffer = new byte[1];
        _ = await stream.ReadAsync(buffer, cancellationToken);
    }

    private async Task<SaveSnapshotFile> CopyFileAsync(
        string sourceRoot,
        string sourcePath,
        string snapshotDirectory,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var safeSourcePath = _pathGuard.EnsureChildPath(sourceRoot, sourcePath);
        var relativePath = Path.GetRelativePath(sourceRoot, safeSourcePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("存档文件越过世界目录。" );

        var destinationPath = Path.Combine(snapshotDirectory, relativePath);
        _pathGuard.EnsureChildPath(snapshotDirectory, destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var beforeCopy = SaveFileFingerprint.Read(safeSourcePath);
        await using var source = new FileStream(
            safeSourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            useAsync: true);
        if (source.Length > maximumBytes)
            throw new InvalidOperationException($"文件 {relativePath} 超过单文件大小上限。" );

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidOperationException($"文件 {relativePath} 在复制过程中超过单文件大小上限。" );
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);

        var afterCopy = SaveFileFingerprint.Read(safeSourcePath);
        if (beforeCopy != afterCopy || total != afterCopy.Length)
        {
            destination.Close();
            File.Delete(destinationPath);
            throw new InvalidOperationException($"文件 {relativePath} 在快照复制期间发生变化，请等待下一次解析。" );
        }

        return new SaveSnapshotFile(
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            destinationPath,
            total,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            afterCopy.LastWriteTimeUtc);
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean incomplete save snapshot {Path}.", path);
        }
    }
}
