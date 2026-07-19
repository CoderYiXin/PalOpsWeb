using PalOps.Web.Settings;

namespace PalOps.Web.SaveGames;

public interface ISaveSourceResolver
{
    Task<IReadOnlyList<SaveWorldCandidate>> DiscoverAsync(
        SaveGameSettings settings,
        CancellationToken cancellationToken = default);

    SaveWorldCandidate ResolveConfigured(SaveGameSettings settings);
}

public sealed class SaveSourceResolver(
    ISavePathGuard pathGuard,
    IHostEnvironment environment,
    ILogger<SaveSourceResolver> logger) : ISaveSourceResolver
{
    public Task<IReadOnlyList<SaveWorldCandidate>> DiscoverAsync(
        SaveGameSettings settings,
        CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, SaveWorldCandidate>(PathComparer);
        if (!string.IsNullOrWhiteSpace(settings.WorldDirectory))
        {
            TryAddWorld(pathGuard.NormalizeWorldDirectory(settings.WorldDirectory), true, candidates);
        }

        foreach (var root in CandidateRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDiscoverFromRoot(root, candidates);
        }

        return Task.FromResult<IReadOnlyList<SaveWorldCandidate>>(
            candidates.Values
                .OrderByDescending(candidate => candidate.Configured)
                .ThenByDescending(candidate => candidate.ModifiedAt)
                .ToArray());
    }

    public SaveWorldCandidate ResolveConfigured(SaveGameSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WorldDirectory))
            throw new InvalidOperationException("尚未配置世界存档目录。");

        var worldDirectory = pathGuard.NormalizeWorldDirectory(settings.WorldDirectory);
        return CreateCandidate(worldDirectory, true)
            ?? throw new DirectoryNotFoundException($"配置的目录不是有效 Palworld 世界存档：{worldDirectory}");
    }

    private IEnumerable<string> CandidateRoots()
    {
        foreach (var environmentName in new[] { "PALOPS_PALWORLD_ROOT", "PALWORLD_SERVER_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }

        yield return environment.ContentRootPath;
        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent)) yield return parent;

        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files (x86)\Steam\steamapps\common\PalServer";
            yield return @"C:\Program Files\Steam\steamapps\common\PalServer";
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return Path.Combine(home, "Steam", "steamapps", "common", "PalServer");
                yield return Path.Combine(home, ".steam", "steam", "steamapps", "common", "PalServer");
            }
        }
    }

    private void TryDiscoverFromRoot(string root, Dictionary<string, SaveWorldCandidate> candidates)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var normalizedRoot = Path.GetFullPath(root);
            var directSaveRoot = Path.Combine(normalizedRoot, "Pal", "Saved", "SaveGames", "0");
            if (Directory.Exists(directSaveRoot))
            {
                foreach (var directory in Directory.EnumerateDirectories(directSaveRoot))
                    TryAddWorld(directory, false, candidates);
                return;
            }

            if (Directory.Exists(normalizedRoot))
            {
                TryAddWorld(normalizedRoot, false, candidates);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Save discovery skipped root {Root}.", root);
        }
    }

    private static void TryAddWorld(
        string worldDirectory,
        bool configured,
        Dictionary<string, SaveWorldCandidate> candidates)
    {
        var candidate = CreateCandidate(worldDirectory, configured);
        if (candidate is null) return;

        if (candidates.TryGetValue(candidate.WorldDirectory, out var existing) && existing.Configured)
            return;

        candidates[candidate.WorldDirectory] = candidate;
    }

    private static SaveWorldCandidate? CreateCandidate(string worldDirectory, bool configured)
    {
        if (!Directory.Exists(worldDirectory)) return null;
        var levelPath = Path.Combine(worldDirectory, "Level.sav");
        var playersPath = Path.Combine(worldDirectory, "Players");
        if (!File.Exists(levelPath) || !Directory.Exists(playersPath)) return null;

        var level = new FileInfo(levelPath);
        var worldId = Path.GetFileName(worldDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new SaveWorldCandidate(
            worldId,
            Path.GetFullPath(worldDirectory),
            Path.GetFullPath(levelPath),
            Path.GetFullPath(playersPath),
            level.LastWriteTimeUtc,
            level.Length,
            Directory.EnumerateFiles(playersPath, "*.sav", SearchOption.TopDirectoryOnly).Count(),
            configured);
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
