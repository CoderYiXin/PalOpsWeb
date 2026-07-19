using PalOps.Web.ServerRuntime;

namespace PalOps.Web.PalDefender.Configuration;

public sealed record PalDefenderResolvedPath(
    string Root,
    string RelativePath,
    string FullPath,
    string Kind,
    bool CanDelete,
    string ActivationHint);

public interface IPalDefenderConfigurationPathResolver
{
    Task<string> GetRootAsync(CancellationToken cancellationToken = default);
    Task<PalDefenderResolvedPath> ResolveAsync(string relativePath, CancellationToken cancellationToken = default);
}

public sealed class PalDefenderConfigurationPathResolver(
    IPalServerRuntimeConfigurationStore runtimeConfiguration) : IPalDefenderConfigurationPathResolver
{
    private static readonly Dictionary<string, (string Kind, bool CanDelete, string Hint)> RootFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Config.json"] = ("config", false, "reloadcfg-or-restart"),
            ["WhiteList.json"] = ("whitelist", false, "reloadcfg"),
            ["Banlist.json"] = ("banlist", false, "reloadcfg"),
            ["RESTAPI/RESTConfig.json"] = ("rest-config", false, "restart")
        };

    private static readonly (string Directory, string Kind, string Hint)[] ManagedDirectories =
    [
        ("Pals/ImportRules", "import-rule", "reloadcfg-or-restart"),
        ("Pals/Templates", "pal-template", "reloadcfg-or-restart"),
        ("Pals/Summons", "pal-summon", "reloadcfg-or-restart"),
        ("RESTAPI/Tokens", "rest-token", "restart")
    ];

    public async Task<string> GetRootAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await runtimeConfiguration.GetAsync(cancellationToken);
        var anchors = new[]
        {
            DirectoryName(configuration.ExecutablePath),
            DirectoryName(configuration.ScriptPath),
            configuration.WorkingDirectory
        }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var anchor in anchors)
        {
            var current = new DirectoryInfo(Path.GetFullPath(anchor!));
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                var direct = Path.Combine(current.FullName, "PalDefender");
                if (current.Name.Equals("Win64", StringComparison.OrdinalIgnoreCase) && Directory.Exists(direct))
                    return EnsureSafeRoot(direct);
                var nested = Path.Combine(current.FullName, "Pal", "Binaries", "Win64", "PalDefender");
                if (Directory.Exists(nested)) return EnsureSafeRoot(nested);
            }
        }

        var preferred = anchors.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(preferred))
            throw new InvalidOperationException("尚未配置 PalServer 启动路径，无法定位 PalDefender 配置目录。");
        var expected = Path.Combine(Path.GetFullPath(preferred), "Pal", "Binaries", "Win64", "PalDefender");
        throw new DirectoryNotFoundException($"未找到 PalDefender 配置目录：{expected}");
    }

    public async Task<PalDefenderResolvedPath> ResolveAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var descriptor = Describe(normalized);
        var root = await GetRootAsync(cancellationToken);
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PalDefender 配置路径越界。");
        EnsureNoReparsePoints(root, full);
        return new PalDefenderResolvedPath(root, normalized, full, descriptor.Kind, descriptor.CanDelete, descriptor.Hint);
    }

    public static IReadOnlyList<string> ManagedDirectoryNames => ManagedDirectories.Select(static item => item.Directory).ToArray();

    public static (string Kind, bool CanDelete, string Hint) Describe(string normalized)
    {
        if (RootFiles.TryGetValue(normalized, out var root)) return root;
        foreach (var managed in ManagedDirectories)
        {
            var prefix = managed.Directory + "/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = normalized[prefix.Length..];
            if (fileName.Length == 0 || fileName.Contains('/') || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                break;
            if (managed.Kind == "rest-token" && fileName.Equals("TokenExample.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("TokenExample.json 是 PalDefender 示例文件，不会被当作有效令牌，不能由 PalOps 管理。");
            return (managed.Kind, true, managed.Hint);
        }
        throw new InvalidOperationException("该文件不在 PalDefender 配置管理白名单中。");
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("配置文件路径不能为空。", nameof(relativePath));
        var normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new InvalidOperationException("配置文件路径无效。");
        if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只允许管理 JSON 配置文件。");
        Describe(normalized);
        return normalized;
    }

    private static string EnsureSafeRoot(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var attributes = File.GetAttributes(full);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("PalDefender 配置根目录不能是符号链接或重解析点。");
        return full;
    }

    private static void EnsureNoReparsePoints(string root, string fullPath)
    {
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("配置路径不能包含符号链接或重解析点。");
        }
    }

    private static string? DirectoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            return File.Exists(full) || Path.HasExtension(full) ? Path.GetDirectoryName(full) : full;
        }
        catch { return null; }
    }
}
