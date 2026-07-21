using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Settings;

namespace PalOps.Web.PalworldConfiguration;

public sealed record PalworldConfigurationPaths(
    string ConfigurationPath,
    string Source,
    string WorldOptionPath);

public interface IPalworldConfigurationPathResolver
{
    Task<PalworldConfigurationPaths> ResolveAsync(CancellationToken cancellationToken = default);
    Task<PalworldConfigurationPaths> SetExplicitPathAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class PalworldConfigurationPathResolver : IPalworldConfigurationPathResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _preferencePath;
    private readonly IPalServerRuntimeConfigurationStore _runtimeStore;
    private readonly IServerSettingsStore _settingsStore;
    private readonly IHostEnvironment _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PalworldConfigurationPathResolver(
        IHostEnvironment environment,
        IOptions<AppRuntimeOptions> options,
        IPalServerRuntimeConfigurationStore runtimeStore,
        IServerSettingsStore settingsStore)
    {
        _environment = environment;
        _runtimeStore = runtimeStore;
        _settingsStore = settingsStore;
        var data = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(data);
        _preferencePath = Path.Combine(data, "palworld-configuration-path.json");
    }

    public async Task<PalworldConfigurationPaths> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var explicitPath = await ReadExplicitPathAsync(cancellationToken);
        var settings = await _settingsStore.GetAsync(cancellationToken);
        var worldOption = ResolveWorldOption(settings.SaveGame.WorldDirectory);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return new(ValidatePath(explicitPath), "explicit", worldOption);

        var runtime = await _runtimeStore.GetAsync(cancellationToken);
        foreach (var candidate in RuntimeCandidates(runtime))
        {
            if (File.Exists(candidate)) return new(candidate, "runtime", worldOption);
        }
        foreach (var candidate in SaveCandidates(settings.SaveGame.WorldDirectory))
        {
            if (File.Exists(candidate)) return new(candidate, "save-world", worldOption);
        }
        foreach (var root in EnvironmentRoots())
        {
            var candidate = Path.Combine(root, "Pal", "Saved", "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini");
            if (File.Exists(candidate)) return new(ValidatePath(candidate), "environment", worldOption);
        }

        var inferred = RuntimeCandidates(runtime).FirstOrDefault()
            ?? SaveCandidates(settings.SaveGame.WorldDirectory).FirstOrDefault()
            ?? EnvironmentRoots().Select(root => Path.Combine(root, "Pal", "Saved", "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini")).FirstOrDefault()
            ?? Path.Combine(_environment.ContentRootPath, "PalWorldSettings.ini");
        return new(ValidatePath(inferred), "inferred", worldOption);
    }

    public async Task<PalworldConfigurationPaths> SetExplicitPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = ValidatePath(path);
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            temporary = _preferencePath + ".tmp-" + Guid.NewGuid().ToString("N");
            var model = new PathPreference(1, normalized, DateTimeOffset.UtcNow);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, model, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, _preferencePath, true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch { }
            }
            _gate.Release();
        }
        var settings = await _settingsStore.GetAsync(cancellationToken);
        return new(normalized, "explicit", ResolveWorldOption(settings.SaveGame.WorldDirectory));
    }

    private async Task<string> ReadExplicitPathAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_preferencePath)) return string.Empty;
            await using var stream = File.OpenRead(_preferencePath);
            var preference = await JsonSerializer.DeserializeAsync<PathPreference>(stream, JsonOptions, cancellationToken);
            return preference?.SchemaVersion == 1 ? preference.Path : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
        finally { _gate.Release(); }
    }

    private static IEnumerable<string> RuntimeCandidates(PalServerRuntimeConfiguration runtime)
    {
        if (!runtime.Confirmed || string.IsNullOrWhiteSpace(runtime.WorkingDirectory)) yield break;
        var working = Path.GetFullPath(runtime.WorkingDirectory);
        foreach (var root in PossibleServerRoots(working))
        {
            yield return ValidatePath(Path.Combine(root, "Pal", "Saved", "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini"));
            var windows = Path.Combine(root, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
            if (!windows.Equals(Path.Combine(root, "Pal", "Saved", "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini"), StringComparison.OrdinalIgnoreCase))
                yield return ValidatePath(windows);
            var linux = Path.Combine(root, "Pal", "Saved", "Config", "LinuxServer", "PalWorldSettings.ini");
            if (!linux.Equals(Path.Combine(root, "Pal", "Saved", "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini"), StringComparison.OrdinalIgnoreCase))
                yield return ValidatePath(linux);
        }
    }

    private static IEnumerable<string> SaveCandidates(string worldDirectory)
    {
        if (string.IsNullOrWhiteSpace(worldDirectory)) yield break;
        var current = new DirectoryInfo(Path.GetFullPath(worldDirectory));
        while (current is not null && !current.Name.Equals("Saved", StringComparison.OrdinalIgnoreCase)) current = current.Parent;
        if (current is null) yield break;
        yield return ValidatePath(Path.Combine(current.FullName, "Config", PlatformConfigurationDirectory(), "PalWorldSettings.ini"));
        yield return ValidatePath(Path.Combine(current.FullName, "Config", "WindowsServer", "PalWorldSettings.ini"));
        yield return ValidatePath(Path.Combine(current.FullName, "Config", "LinuxServer", "PalWorldSettings.ini"));
    }

    private IEnumerable<string> EnvironmentRoots()
    {
        foreach (var variable in new[] { "PALWORLD_SERVER_ROOT", "PALOPS_PALWORLD_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) yield return Path.GetFullPath(value);
        }
        yield return _environment.ContentRootPath;
    }

    private static IEnumerable<string> PossibleServerRoots(string working)
    {
        yield return working;
        var current = new DirectoryInfo(working);
        for (var index = 0; index < 5 && current.Parent is not null; index++)
        {
            current = current.Parent;
            yield return current.FullName;
        }
    }

    private static string ResolveWorldOption(string worldDirectory)
    {
        if (string.IsNullOrWhiteSpace(worldDirectory)) return string.Empty;
        return Path.Combine(Path.GetFullPath(worldDirectory), "WorldOption.sav");
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_PATH_INVALID", "PalWorldSettings.ini 路径不能为空。");
        string full;
        try { full = Path.GetFullPath(path.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_PATH_INVALID", "PalWorldSettings.ini 路径无效。", path, null, ex);
        }
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_NETWORK_PATH_UNSUPPORTED", "PalWorldSettings.ini 不能使用网络共享路径。", full);
        if (!Path.GetFileName(full).Equals("PalWorldSettings.ini", StringComparison.OrdinalIgnoreCase))
            throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_PATH_INVALID", "配置文件名必须为 PalWorldSettings.ini。", full);
        EnsureNoReparsePoints(full);
        return full;
    }

    private static void EnsureNoReparsePoints(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_PATH_INVALID", "PalWorldSettings.ini 路径缺少有效根目录。", fullPath);
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_REPARSE_POINT_UNSUPPORTED", "配置路径不能包含符号链接或重解析点。", current);
        }
    }

    private static string PlatformConfigurationDirectory() => OperatingSystem.IsWindows() ? "WindowsServer" : "LinuxServer";
    private sealed record PathPreference(int SchemaVersion, string Path, DateTimeOffset UpdatedAt);
}
