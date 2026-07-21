using PalOps.Web.Infrastructure;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.PluginManagement;

public sealed record PluginManagementPaths(
    string ServerRoot,
    string BinariesRoot,
    string ModsRoot,
    string PluginsRoot,
    string DataRoot,
    string StagingRoot,
    string BackupRoot);

public interface IPluginManagementPathResolver
{
    Task<PluginManagementPaths> ResolveAsync(CancellationToken cancellationToken = default);
    string ResolveInstallDirectory(PluginManagementPaths paths, string relativePath, PluginPackageKind kind);
    string ResolveServerRelativePath(PluginManagementPaths paths, string relativePath);
    string ToServerRelativePath(PluginManagementPaths paths, string fullPath);
    void EnsureSafeExistingPath(string fullPath);
}

public sealed class PluginManagementPathResolver(
    IPalServerRuntimeConfigurationStore runtimeConfiguration,
    IRuntimePathResolver runtimePaths) : IPluginManagementPathResolver
{
    public async Task<PluginManagementPaths> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await runtimeConfiguration.GetAsync(cancellationToken);
        if (!configuration.Confirmed)
            throw new PluginManagementException(409, "PLUGIN_SERVER_CONFIGURATION_REQUIRED", "请先确认 PalServer 启动配置。", suggestedAction: "在服务器运行配置页确认 PalServer.exe、工作目录和启动参数。");

        var root = ResolveServerRoot(configuration);
        EnsureLocalPath(root, "PalServer 根目录");
        if (!Directory.Exists(root))
            throw new PluginManagementException(422, "PLUGIN_SERVER_ROOT_NOT_FOUND", "PalServer 根目录不存在。", root);
        EnsureNoReparsePoints(root);

        var binaries = Path.Combine(root, "Pal", "Binaries", "Win64");
        var mods = Path.Combine(binaries, "Mods");
        var plugins = Path.Combine(binaries, "Plugins");
        var data = runtimePaths.ResolveDataPath("plugin-management");
        var staging = Path.Combine(data, "staging");
        var backups = Path.Combine(data, "backups");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backups);
        return new(root, binaries, mods, plugins, data, staging, backups);
    }

    public string ResolveInstallDirectory(PluginManagementPaths paths, string relativePath, PluginPackageKind kind)
    {
        var normalized = NormalizeRelativePath(relativePath, "安装目录");
        var full = ResolveUnderRoot(paths.ServerRoot, normalized, "安装目录");
        var relative = NormalizeSeparators(Path.GetRelativePath(paths.ServerRoot, full));
        var binariesRelative = "Pal/Binaries/Win64";
        var allowed = kind == PluginPackageKind.UE4SS
            ? relative.Equals(binariesRelative, StringComparison.OrdinalIgnoreCase)
            : relative.StartsWith(binariesRelative + "/Mods/", StringComparison.OrdinalIgnoreCase)
              || relative.StartsWith(binariesRelative + "/Plugins/", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new PluginManagementException(
                422,
                "PLUGIN_INSTALL_DIRECTORY_NOT_ALLOWED",
                "插件安装目录不在允许范围内。",
                relative,
                kind == PluginPackageKind.UE4SS
                    ? "UE4SS 只能安装到 Pal/Binaries/Win64。"
                    : "其他插件只能安装到 Pal/Binaries/Win64/Mods 或 Pal/Binaries/Win64/Plugins 的子目录。");
        }
        EnsureSafeExistingPath(full);
        return full;
    }

    public string ResolveServerRelativePath(PluginManagementPaths paths, string relativePath)
    {
        var full = ResolveUnderRoot(paths.ServerRoot, NormalizeRelativePath(relativePath, "服务器相对路径"), "服务器相对路径");
        EnsureSafeExistingPath(full);
        return full;
    }

    public string ToServerRelativePath(PluginManagementPaths paths, string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        EnsureContained(paths.ServerRoot, normalized, "服务器文件路径");
        return NormalizeSeparators(Path.GetRelativePath(paths.ServerRoot, normalized));
    }

    public void EnsureSafeExistingPath(string fullPath)
    {
        EnsureLocalPath(fullPath, "插件路径");
        EnsureNoReparsePoints(fullPath);
    }

    private static string ResolveServerRoot(PalServerRuntimeConfiguration configuration)
    {
        var anchors = new[]
        {
            configuration.WorkingDirectory,
            configuration.ExecutablePath,
            configuration.ScriptPath
        };
        foreach (var anchor in anchors.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            string full;
            try { full = Path.GetFullPath(anchor.Trim()); }
            catch { continue; }
            var directory = File.Exists(full) ? Path.GetDirectoryName(full) : full;
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var current = new DirectoryInfo(directory);
            for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Pal", "Binaries", "Win64")))
                    return Path.TrimEndingDirectorySeparator(current.FullName);
                if (File.Exists(Path.Combine(current.FullName, "PalServer.exe")) && Directory.Exists(Path.Combine(current.FullName, "Pal")))
                    return Path.TrimEndingDirectorySeparator(current.FullName);
            }
        }
        throw new PluginManagementException(
            422,
            "PLUGIN_SERVER_ROOT_UNRESOLVED",
            "无法从当前启动配置推导 PalServer 根目录。",
            configuration.WorkingDirectory,
            "确认工作目录位于 PalServer 安装目录，且目录中包含 Pal/Binaries/Win64。");
    }

    private static string ResolveUnderRoot(string root, string relativePath, string label)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(root, full, label);
        EnsureLocalPath(full, label);
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static void EnsureContained(string root, string full, string label)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(full));
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new PluginManagementException(422, "PLUGIN_PATH_ESCAPE", $"{label}越过 PalServer 根目录。", full);
    }

    public static string NormalizeRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PluginManagementException(422, "PLUGIN_PATH_INVALID", $"{label}不能为空。");
        var normalized = NormalizeSeparators(value.Trim());
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':')
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
            throw new PluginManagementException(422, "PLUGIN_PATH_INVALID", $"{label}必须是安全的相对路径。", value);
        return string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeSeparators(string value) => value.Replace('\\', '/');

    private static void EnsureLocalPath(string path, string label)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)))
            throw new PluginManagementException(422, "PLUGIN_NETWORK_PATH_UNSUPPORTED", $"{label}不能使用网络共享路径。", path);
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                if (new DriveInfo(root).DriveType == DriveType.Network)
                    throw new PluginManagementException(422, "PLUGIN_NETWORK_PATH_UNSUPPORTED", $"{label}不能使用网络映射盘。", path);
            }
            catch (PluginManagementException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    private static void EnsureNoReparsePoints(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root))
            throw new PluginManagementException(422, "PLUGIN_PATH_INVALID", "插件路径缺少有效根目录。", full);
        var current = root;
        var relative = Path.GetRelativePath(root, full);
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new PluginManagementException(422, "PLUGIN_REPARSE_POINT_UNSUPPORTED", "插件路径不能包含符号链接或重解析点。", current);
        }
    }
}
