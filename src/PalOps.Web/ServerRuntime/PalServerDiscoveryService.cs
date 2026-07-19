using System.Security.Cryptography;
using System.Text;
using PalOps.Web.Settings;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerDiscoveryService
{
    Task<PalServerDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);
}

public sealed class PalServerDiscoveryService(IServerSettingsStore settingsStore) : IPalServerDiscoveryService
{
    private static readonly string[] NegativeNames = ["update", "backup", "install", "steamcmd", "restore", "fetch"];

    public async Task<PalServerDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PalServerRuntimeException(409, "PALSERVER_WINDOWS_REQUIRED", "进程管理仅支持 Windows 本机部署。");

        var settings = await settingsStore.GetAsync(cancellationToken);
        var configured = settings.SaveGame.WorldDirectory?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured))
            throw new PalServerRuntimeException(422, "PALSERVER_SAVE_PATH_NOT_CONFIGURED", "请先配置存档目录。");

        string full;
        try { full = Path.GetFullPath(configured); }
        catch (Exception ex)
        {
            throw new PalServerRuntimeException(422, "PALSERVER_SAVE_PATH_INVALID", "存档目录不是有效的本机路径。", configured, null, ex);
        }
        if (IsUncPath(full))
            throw new PalServerRuntimeException(422, "PALSERVER_NETWORK_PATH_UNSUPPORTED", "存档目录不能使用网络共享路径。", full);

        var warnings = new List<string>();
        if (!Directory.Exists(full)) warnings.Add("已配置的存档目录当前不可访问。");
        var startDirectory = Directory.Exists(full)
            ? new DirectoryInfo(full)
            : new DirectoryInfo(Path.GetDirectoryName(full) ?? full);
        var ancestors = Ancestors(startDirectory).Take(12).ToArray();
        var executable = FindExecutable(ancestors, cancellationToken);
        if (executable is null)
            return new(full, null, [], warnings.Append("未找到 PalServer.exe，请手动选择路径。").ToArray());

        var serverRoot = Path.GetDirectoryName(executable)!;
        var candidates = new List<PalServerDiscoveryCandidate>();
        foreach (var script in EnumerateScripts(serverRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(script).ToLowerInvariant();
            if (NegativeNames.Any(fragment => name.Contains(fragment, StringComparison.Ordinal))) continue;

            var score = 60;
            var reasons = new List<string> { "发现本地启动脚本" };
            if (name.Contains("start", StringComparison.Ordinal)) { score += 25; reasons.Add("名称包含 start"); }
            if (name.Contains("pal", StringComparison.Ordinal) || name.Contains("server", StringComparison.Ordinal))
            { score += 10; reasons.Add("名称匹配 PalServer"); }
            if (string.Equals(Path.GetDirectoryName(script), serverRoot, StringComparison.OrdinalIgnoreCase))
            { score += 10; reasons.Add("与 PalServer.exe 同目录"); }
            candidates.Add(new(
                CandidateId(script),
                PalServerLaunchMode.Script,
                executable,
                script,
                Path.GetDirectoryName(script)!,
                string.Empty,
                score,
                reasons));
        }

        candidates.Add(new(CandidateId(executable), PalServerLaunchMode.Executable, executable, string.Empty,
            serverRoot, string.Empty, 50, ["直接启动 PalServer.exe"]));

        return new(full, serverRoot,
            candidates.OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.ScriptPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(), warnings);
    }

    private static string? FindExecutable(IEnumerable<DirectoryInfo> ancestors, CancellationToken cancellationToken)
    {
        foreach (var directory in ancestors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var direct = Path.Combine(directory.FullName, "PalServer.exe");
            if (File.Exists(direct)) return direct;

            try
            {
                foreach (var child in directory.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var childExecutable = Path.Combine(child.FullName, "PalServer.exe");
                    if (File.Exists(childExecutable)) return childExecutable;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        return null;
    }

    private static IEnumerable<DirectoryInfo> Ancestors(DirectoryInfo? directory)
    {
        while (directory is not null)
        {
            yield return directory;
            directory = directory.Parent;
        }
    }

    private static IEnumerable<string> EnumerateScripts(string serverRoot)
    {
        var directories = new List<string> { serverRoot };
        try
        {
            directories.AddRange(Directory.EnumerateDirectories(serverRoot)
                .Where(path => Path.GetFileName(path).Contains("script", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> scripts;
            try
            {
                scripts = Directory.EnumerateFiles(directory, "*.bat", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(directory, "*.cmd", SearchOption.TopDirectoryOnly))
                    .ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var script in scripts) yield return script;
        }
    }

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal));

    private static string CandidateId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
}
