using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Versioning;

namespace PalOps.Web.PluginManagement;

public interface IPluginInventoryScanner
{
    Task<PluginManagementDashboard> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed partial class PluginInventoryScanner(
    IPluginManagementPathResolver pathResolver,
    IPluginManagementRepository repository,
    IPalServerRuntimeConfigurationStore runtimeConfiguration,
    IPalServerRuntimeCoordinator runtimeCoordinator,
    ILogger<PluginInventoryScanner> logger) : IPluginInventoryScanner
{
    private const int MaximumFiles = 20_000;
    private const long MaximumBytes = 4L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PluginManagementDashboard> ScanAsync(CancellationToken cancellationToken = default)
    {
        var paths = await pathResolver.ResolveAsync(cancellationToken);
        var state = await repository.GetAsync(cancellationToken);
        var configuration = await runtimeConfiguration.GetAsync(cancellationToken);
        var runtime = runtimeCoordinator.Current;
        var serverRunning = runtime.Process.ProcessId.HasValue
            && !runtime.State.Equals(PalServerRuntimeState.Stopped.ToString(), StringComparison.OrdinalIgnoreCase);
        var gameVersion = DetectGameVersion(configuration, paths);
        var warnings = new List<string>();
        var candidates = new List<Candidate>();

        foreach (var registration in state.Packages.Values.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try { candidates.Add(await RegisteredCandidateAsync(paths, registration, cancellationToken)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or PluginManagementException)
            {
                logger.LogWarning(ex, "Unable to scan managed plugin {PluginId}.", registration.Id);
                candidates.Add(new(
                    registration.Id, registration.Name, registration.Kind, registration.Version,
                    registration.InstallDirectory, string.Empty, registration.Enabled, true,
                    registration, string.Empty, 0, 0, null, [Limit(ex.Message, 300)]));
            }
        }

        DiscoverPalDefender(paths, candidates, state, warnings);
        DiscoverUe4ss(paths, candidates, state);
        DiscoverPakMods(paths, candidates, state, warnings);
        DiscoverDirectories(paths, paths.ModsRoot, PluginPackageKind.ServerMod, candidates, state, warnings);
        DiscoverDirectories(paths, paths.PluginsRoot, PluginPackageKind.DllPlugin, candidates, state, warnings);
        DiscoverLooseFiles(paths, paths.ModsRoot, candidates, state, warnings);
        DiscoverLooseFiles(paths, paths.PluginsRoot, candidates, state, warnings);

        if (candidates.Count == 0)
        {
            warnings.Add(
                "未检测到可识别组件。已扫描 Pal/Binaries/Win64/PalDefender、UE4SS 根文件、Mods、Plugins、Pal/Content/Paks/~mods 和 LogicMods。"
            );
        }

        var deduplicated = candidates
            .GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(item => item.Managed).ThenByDescending(item => item.Enabled).First())
            .ToList();
        var byId = deduplicated.ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);
        var inventory = deduplicated.Select(candidate => ToInventory(candidate, byId, state, gameVersion)).OrderBy(static item => item.Kind).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        return new(
            paths.ServerRoot,
            gameVersion,
            serverRunning,
            DateTimeOffset.UtcNow,
            inventory,
            state.Backups.OrderByDescending(static item => item.CreatedAt).Take(50).ToArray(),
            state.History.OrderByDescending(static item => item.CompletedAt).Take(200).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray());
    }

    private async Task<Candidate> RegisteredCandidateAsync(PluginManagementPaths paths, ManagedPluginRegistration registration, CancellationToken cancellationToken)
    {
        var installRoot = pathResolver.ResolveInstallDirectory(paths, registration.InstallDirectory, registration.Kind);
        var existingFiles = new List<(string Relative, string Full)>();
        var enabledCount = 0;
        var disabledCount = 0;
        foreach (var relative in registration.InstalledFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = pathResolver.ResolveServerRelativePath(paths, relative);
            var disabled = full + ".palops-disabled";
            if (File.Exists(full)) { existingFiles.Add((relative, full)); enabledCount++; }
            else if (File.Exists(disabled))
            {
                pathResolver.EnsureSafeExistingPath(disabled);
                existingFiles.Add((relative, disabled));
                disabledCount++;
            }
        }
        var enabled = existingFiles.Count == 0 ? registration.Enabled : enabledCount >= disabledCount;
        var measure = await MeasureFilesAsync(paths.ServerRoot, existingFiles, cancellationToken);
        var primary = existingFiles.FirstOrDefault().Full;
        if (string.IsNullOrWhiteSpace(primary)) primary = installRoot;
        var warnings = new List<string>();
        if (existingFiles.Count < registration.InstalledFiles.Count)
            warnings.Add($"受管文件缺失：{registration.InstalledFiles.Count - existingFiles.Count} 个。");
        if (enabledCount > 0 && disabledCount > 0)
            warnings.Add("插件文件处于部分启用状态，需要先修复文件一致性。");
        return new(
            registration.Id, registration.Name, registration.Kind,
            DetectVersion(primary, registration.Version), registration.InstallDirectory,
            primary, enabled, true, registration,
            measure.Hash, measure.Size, measure.Count, measure.LastWriteAt, warnings);
    }

    private static void DiscoverPalDefender(
        PluginManagementPaths paths,
        List<Candidate> candidates,
        PluginManagementState state,
        List<string> warnings)
    {
        if (state.Packages.ContainsKey("paldefender")
            || candidates.Any(item => item.Id.Equals("paldefender", StringComparison.OrdinalIgnoreCase)))
            return;

        var directory = Path.Combine(paths.BinariesRoot, "PalDefender");
        var disabledDirectory = directory + ".palops-disabled";
        var enabled = Directory.Exists(directory);
        var selectedDirectory = enabled ? directory : Directory.Exists(disabledDirectory) ? disabledDirectory : string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            try
            {
                if ((File.GetAttributes(selectedDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"已跳过 PalDefender 重解析点目录：{selectedDirectory}");
                    return;
                }
                var primary = EnumerateSafeFiles(selectedDirectory)
                    .FirstOrDefault(path => Path.GetFileName(path).Equals("PalDefender.dll", StringComparison.OrdinalIgnoreCase))
                    ?? selectedDirectory;
                var info = MeasureDirectorySync(paths.ServerRoot, selectedDirectory);
                candidates.Add(new(
                    "paldefender", "PalDefender", PluginPackageKind.PalDefender,
                    DetectVersion(primary, string.Empty),
                    Normalize(Path.GetRelativePath(paths.ServerRoot, directory)),
                    primary, enabled, false, null,
                    info.Hash, info.Size, info.Count, info.LastWriteAt,
                    ["当前 PalDefender 由外部安装，PalOps 仅检测版本与文件状态。"]));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                warnings.Add($"无法扫描 PalDefender：{Limit(ex.Message, 200)}");
                return;
            }
        }

        foreach (var file in new[]
        {
            Path.Combine(paths.BinariesRoot, "PalDefender.dll"),
            Path.Combine(paths.BinariesRoot, "PalDefender.dll.palops-disabled")
        })
        {
            if (!File.Exists(file)) continue;
            try
            {
                var info = MeasureFilesSync(paths.ServerRoot, [file]);
                candidates.Add(new(
                    "paldefender", "PalDefender", PluginPackageKind.PalDefender,
                    DetectVersion(file, string.Empty), "Pal/Binaries/Win64",
                    file, !file.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase), false, null,
                    info.Hash, info.Size, info.Count, info.LastWriteAt,
                    ["检测到外部安装的 PalDefender DLL。"]));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                warnings.Add($"无法扫描 PalDefender DLL：{Limit(ex.Message, 200)}");
            }
            return;
        }
    }

    private static void DiscoverPakMods(
        PluginManagementPaths paths,
        List<Candidate> candidates,
        PluginManagementState state,
        List<string> warnings)
    {
        var roots = new[]
        {
            Path.Combine(paths.ServerRoot, "Pal", "Content", "Paks", "~mods"),
            Path.Combine(paths.ServerRoot, "Pal", "Content", "Paks", "LogicMods")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            DiscoverDirectories(paths, root, PluginPackageKind.ServerMod, candidates, state, warnings);
            try
            {
                var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(static file => IsPakModFile(file))
                    .GroupBy(static file => PakModBaseName(file), StringComparer.OrdinalIgnoreCase);
                foreach (var group in files)
                {
                    var display = group.Key;
                    var id = "pak-" + Slugify(display);
                    if (state.Packages.ContainsKey(id)
                        || candidates.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var groupedFiles = group.ToArray();
                    var enabledFiles = groupedFiles.Where(static file => !file.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var info = MeasureFilesSync(paths.ServerRoot, groupedFiles);
                    var primary = groupedFiles.FirstOrDefault(file => file.Contains(".pak", StringComparison.OrdinalIgnoreCase)) ?? groupedFiles[0];
                    candidates.Add(new(
                        id, display, PluginPackageKind.ServerMod, DetectVersion(primary, string.Empty),
                        Normalize(Path.GetRelativePath(paths.ServerRoot, root)), primary,
                        enabledFiles.Length > 0, false, null,
                        info.Hash, info.Size, info.Count, info.LastWriteAt,
                        ["检测到外部安装的 PAK/IoStore 模组，PalOps 不会直接修改它。"]));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                warnings.Add($"无法扫描 PAK 模组目录 {root}：{Limit(ex.Message, 200)}");
            }
        }
    }

    private static bool IsPakModFile(string path)
    {
        var normalized = path.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".palops-disabled".Length]
            : path;
        var extension = Path.GetExtension(normalized);
        return extension.Equals(".pak", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase);
    }

    private static string PakModBaseName(string path)
    {
        var normalized = path.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".palops-disabled".Length]
            : path;
        return Path.GetFileNameWithoutExtension(normalized);
    }

    private static void DiscoverUe4ss(PluginManagementPaths paths, List<Candidate> candidates, PluginManagementState state)
    {
        if (state.Packages.ContainsKey("ue4ss")) return;
        var known = new[] { "UE4SS.dll", "dwmapi.dll", "UE4SS-settings.ini" };
        var enabled = known.Select(name => Path.Combine(paths.BinariesRoot, name)).Where(File.Exists).ToArray();
        var disabled = known.Select(name => Path.Combine(paths.BinariesRoot, name) + ".palops-disabled").Where(File.Exists).ToArray();
        var files = enabled.Concat(disabled).ToArray();
        if (files.Length == 0) return;
        var primary = files.First();
        var info = MeasureFilesSync(paths.ServerRoot, files);
        candidates.Add(new("ue4ss", "UE4SS", PluginPackageKind.UE4SS, DetectVersion(primary, string.Empty),
            "Pal/Binaries/Win64", primary, enabled.Length >= disabled.Length, false, null,
            info.Hash, info.Size, info.Count, info.LastWriteAt, ["当前 UE4SS 由外部安装，上传带 palops-package.json 的包后才能由 PalOps 变更。"]));
    }

    private static void DiscoverDirectories(
        PluginManagementPaths paths,
        string root,
        PluginPackageKind defaultKind,
        List<Candidate> candidates,
        PluginManagementState state,
        List<string> warnings)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"已跳过重解析点目录：{directory}");
                    continue;
                }
                var name = Path.GetFileName(directory);
                var disabled = name.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase);
                var display = disabled ? name[..^".palops-disabled".Length] : name;
                var id = Slugify(display);
                if (state.Packages.ContainsKey(id) || candidates.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                var kind = display.Equals("PalDefender", StringComparison.OrdinalIgnoreCase)
                    ? PluginPackageKind.PalDefender
                    : InferDirectoryKind(directory, defaultKind);
                var info = MeasureDirectorySync(paths.ServerRoot, directory);
                candidates.Add(new(id, display, kind, DetectVersion(directory, string.Empty),
                    Normalize(Path.GetRelativePath(paths.ServerRoot, directory)), directory, !disabled, false, null,
                    info.Hash, info.Size, info.Count, info.LastWriteAt,
                    ["当前插件由外部安装，上传带 palops-package.json 的包后才能由 PalOps 变更。"]));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法扫描目录 {root}：{Limit(ex.Message, 200)}");
        }
    }

    private static void DiscoverLooseFiles(
        PluginManagementPaths paths,
        string root,
        List<Candidate> candidates,
        PluginManagementState state,
        List<string> warnings)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var kind = extension switch
                {
                    ".dll" => PluginPackageKind.DllPlugin,
                    ".lua" or ".js" or ".ps1" or ".cmd" or ".bat" => PluginPackageKind.ScriptPlugin,
                    ".pak" => PluginPackageKind.ServerMod,
                    _ => (PluginPackageKind?)null
                };
                if (kind is null) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                var id = "loose-" + Slugify(name);
                if (state.Packages.ContainsKey(id) || candidates.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                var info = MeasureFilesSync(paths.ServerRoot, [file]);
                candidates.Add(new(id, name, kind.Value, DetectVersion(file, string.Empty),
                    Normalize(Path.GetRelativePath(paths.ServerRoot, root)), file, true, false, null,
                    info.Hash, info.Size, info.Count, info.LastWriteAt,
                    ["检测到未受管的散装插件文件，PalOps 不会直接修改它。"]));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法扫描散装插件 {root}：{Limit(ex.Message, 200)}");
        }
    }

    private static PluginInventoryItem ToInventory(
        Candidate candidate,
        IReadOnlyDictionary<string, Candidate> all,
        PluginManagementState state,
        string gameVersion)
    {
        var dependencies = (candidate.Registration?.Dependencies ?? Array.Empty<PluginDependencyManifest>())
            .Select(dependency => DependencyStatus(dependency, all))
            .ToArray();
        var release = state.Releases.GetValueOrDefault(candidate.Id);
        var (compatibility, compatibilityMessage) = Compatibility(candidate.Registration?.CompatibleGameVersions ?? Array.Empty<string>(), gameVersion);
        return new(
            candidate.Id,
            candidate.Name,
            candidate.Kind,
            candidate.Version,
            release?.LatestVersion ?? string.Empty,
            IsUpdateAvailable(candidate.Version, release?.LatestVersion),
            release?.ReleaseUrl ?? string.Empty,
            release?.Message,
            candidate.Enabled,
            candidate.Managed,
            candidate.Managed && !candidate.Warnings.Any(static warning => warning.Contains("部分启用", StringComparison.Ordinal)),
            candidate.InstallDirectory,
            candidate.PrimaryPath,
            candidate.Sha256,
            candidate.SizeBytes,
            candidate.FileCount,
            candidate.LastWriteAt,
            compatibility,
            compatibilityMessage,
            dependencies,
            candidate.Warnings);
    }

    private static PluginDependencyStatus DependencyStatus(PluginDependencyManifest dependency, IReadOnlyDictionary<string, Candidate> all)
    {
        if (!all.TryGetValue(dependency.PackageId, out var installed))
            return new(dependency.PackageId, dependency.MinimumVersion, dependency.Optional, false, false, string.Empty, dependency.Optional, dependency.Optional ? "optional-missing" : "missing");
        var versionSatisfied = string.IsNullOrWhiteSpace(dependency.MinimumVersion)
            || VersionAtLeast(installed.Version, dependency.MinimumVersion);
        var status = !installed.Enabled ? "disabled" : versionSatisfied ? "satisfied" : "version-too-old";
        return new(dependency.PackageId, dependency.MinimumVersion, dependency.Optional, true, installed.Enabled, installed.Version, versionSatisfied, status);
    }

    private static bool IsUpdateAvailable(string current, string? latest)
    {
        if (!SemanticVersionValue.TryParse(current, out var currentVersion)) return false;
        if (!SemanticVersionValue.TryParse(latest, out var latestVersion)) return false;
        return latestVersion.CompareTo(currentVersion) > 0;
    }

    public static bool VersionAtLeast(string current, string minimum)
    {
        if (!SemanticVersionValue.TryParse(current, out var currentVersion)) return false;
        if (!SemanticVersionValue.TryParse(minimum, out var minimumVersion)) return false;
        return currentVersion.CompareTo(minimumVersion) >= 0;
    }

    public static bool IsGameVersionCompatible(string currentVersion, IReadOnlyList<string> patterns)
        => Compatibility(patterns, currentVersion).Status != "incompatible";

    private static (string Status, string Message) Compatibility(IReadOnlyList<string> patterns, string gameVersion)
    {
        if (patterns.Count == 0) return ("unknown", "插件包未声明游戏版本兼容范围。");
        if (string.IsNullOrWhiteSpace(gameVersion)) return ("unknown", "无法读取当前 PalServer 文件版本。");
        foreach (var pattern in patterns)
        {
            var normalized = pattern.Trim();
            if (normalized == "*" || normalized.Equals(gameVersion, StringComparison.OrdinalIgnoreCase))
                return ("compatible", $"兼容当前游戏版本 {gameVersion}。");
            if (normalized.EndsWith(".*", StringComparison.Ordinal)
                && gameVersion.StartsWith(normalized[..^1], StringComparison.OrdinalIgnoreCase))
                return ("compatible", $"兼容当前游戏版本 {gameVersion}。");
        }
        return ("incompatible", $"包声明的兼容版本为 {string.Join(", ", patterns)}，当前版本为 {gameVersion}。");
    }

    private static string DetectGameVersion(PalServerRuntimeConfiguration configuration, PluginManagementPaths paths)
    {
        foreach (var path in new[]
        {
            configuration.ExecutablePath,
            Path.Combine(paths.ServerRoot, "PalServer.exe"),
            Path.Combine(paths.BinariesRoot, "PalServer-Win64-Shipping-Cmd.exe"),
            Path.Combine(paths.BinariesRoot, "PalServer-Win64-Shipping.exe")
        })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
                if (!string.IsNullOrWhiteSpace(version)) return NormalizeVersion(version);
                version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!string.IsNullOrWhiteSpace(version)) return NormalizeVersion(version);
            }
            catch { }
        }
        return string.Empty;
    }

    private static string DetectVersion(string path, string fallback)
    {
        if (File.Exists(path) && Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
                if (!string.IsNullOrWhiteSpace(version)) return NormalizeVersion(version);
                version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!string.IsNullOrWhiteSpace(version)) return NormalizeVersion(version);
            }
            catch { }
        }
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var manifest = Path.Combine(directory, "palops-package.json");
            if (File.Exists(manifest))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<PluginPackageManifest>(File.ReadAllText(manifest), JsonOptions);
                    if (!string.IsNullOrWhiteSpace(parsed?.Version)) return parsed.Version.Trim();
                }
                catch { }
            }
            var match = VersionPattern().Match(Path.GetFileName(directory));
            if (match.Success) return match.Groups[1].Value;
        }
        return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback.Trim();
    }

    private static string NormalizeVersion(string value)
    {
        var match = VersionPattern().Match(value);
        return match.Success ? match.Groups[1].Value : Limit(value.Trim(), 80);
    }

    public static async Task<string> ComputeTreeHashAsync(string root, CancellationToken cancellationToken = default)
    {
        var files = EnumerateSafeFiles(root).Select(path => (Normalize(Path.GetRelativePath(root, path)), path)).ToArray();
        return (await MeasureFilesAsync(root, files, cancellationToken)).Hash;
    }

    private static async Task<Measure> MeasureFilesAsync(string root, IEnumerable<(string Relative, string Full)> files, CancellationToken cancellationToken)
    {
        var ordered = files.OrderBy(static item => item.Relative, StringComparer.Ordinal).ToArray();
        if (ordered.Length > MaximumFiles) throw new InvalidDataException($"插件文件数量超过 {MaximumFiles} 个限制。");
        long total = 0;
        DateTimeOffset? lastWrite = null;
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(item.Full);
            total = checked(total + info.Length);
            if (total > MaximumBytes) throw new InvalidDataException("插件文件总大小超过 4 GiB 限制。");
            var relativeBytes = Encoding.UTF8.GetBytes(item.Relative);
            aggregate.AppendData(BitConverter.GetBytes(relativeBytes.Length));
            aggregate.AppendData(relativeBytes);
            await using var stream = new FileStream(item.Full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            aggregate.AppendData(hash);
            var write = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (!lastWrite.HasValue || write > lastWrite) lastWrite = write;
        }
        return new(Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant(), total, ordered.Length, lastWrite);
    }

    private static Measure MeasureDirectorySync(string serverRoot, string directory)
    {
        try
        {
            var files = EnumerateSafeFiles(directory).Select(path => (Normalize(Path.GetRelativePath(serverRoot, path)), path));
            return MeasureFilesAsync(serverRoot, files, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch { return new(string.Empty, 0, 0, null); }
    }

    private static Measure MeasureFilesSync(string serverRoot, IEnumerable<string> files)
    {
        try
        {
            var values = files.Where(File.Exists).Select(path => (Normalize(Path.GetRelativePath(serverRoot, path)), path));
            return MeasureFilesAsync(serverRoot, values, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch { return new(string.Empty, 0, 0, null); }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        if (File.Exists(root)) { yield return root; yield break; }
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                if (++count > MaximumFiles) throw new InvalidDataException($"插件文件数量超过 {MaximumFiles} 个限制。");
                yield return file;
            }
        }
    }

    private static PluginPackageKind InferDirectoryKind(string directory, PluginPackageKind fallback)
    {
        try
        {
            foreach (var file in EnumerateSafeFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return PluginPackageKind.DllPlugin;
                if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)) return PluginPackageKind.ScriptPlugin;
            }
        }
        catch { }
        return fallback;
    }

    private static string Slugify(string value)
    {
        var normalized = SlugPattern().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "plugin-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant() : normalized[..Math.Min(normalized.Length, 80)];
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    [GeneratedRegex("([0-9]+(?:\\.[0-9]+){1,3})", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    private sealed record Candidate(
        string Id,
        string Name,
        PluginPackageKind Kind,
        string Version,
        string InstallDirectory,
        string PrimaryPath,
        bool Enabled,
        bool Managed,
        ManagedPluginRegistration? Registration,
        string Sha256,
        long SizeBytes,
        int FileCount,
        DateTimeOffset? LastWriteAt,
        IReadOnlyList<string> Warnings);

    private sealed record Measure(string Hash, long Size, int Count, DateTimeOffset? LastWriteAt);
}
