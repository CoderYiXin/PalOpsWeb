using System.ComponentModel;
using System.Diagnostics;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerProcessLocator
{
    PalServerProcessSnapshot Locate(PalServerRuntimeConfiguration configuration);
    IReadOnlyList<PalServerVerifiedProcess> LocateVerifiedProcesses(PalServerRuntimeConfiguration configuration);
}

public sealed class PalServerProcessLocator(IWindowsProcessTree processTree) : IPalServerProcessLocator
{
    public IReadOnlyList<PalServerVerifiedProcess> LocateVerifiedProcesses(PalServerRuntimeConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var candidates = ReadCandidates();
        var serverRoot = ResolveConfiguredServerRoot(configuration);
        var configuredPath = ResolveConfiguredLauncherPath(configuration, serverRoot);
        return candidates
            .Where(candidate => IsVerifiedShippingCandidate(candidate, serverRoot)
                || (configuredPath is not null
                    && NormalizePath(candidate.ExecutablePath)?.Equals(configuredPath, StringComparison.OrdinalIgnoreCase) == true))
            .Select(candidate => new PalServerVerifiedProcess(
                candidate.ProcessId,
                candidate.ProcessName,
                candidate.ExecutablePath,
                candidate.ParentProcessId,
                !IsShippingProcessName(candidate.ProcessName)))
            .OrderBy(candidate => candidate.IsLauncher ? 1 : 0)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();
    }

    public PalServerProcessSnapshot Locate(PalServerRuntimeConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows())
            return new(null, PalServerRuntimeState.Faulted.ToString(), false, "PalServer 进程管理仅支持 Windows。", null, null, null, 0, null);

        var candidates = ReadCandidates();
        if (candidates.Count == 0) return Stopped();

        var serverRoot = ResolveConfiguredServerRoot(configuration);
        var configuredPath = ResolveConfiguredLauncherPath(configuration, serverRoot);
        var verifiedShipping = candidates
            .Where(candidate => IsVerifiedShippingCandidate(candidate, serverRoot))
            .ToArray();

        if (verifiedShipping.Length == 1)
            return Snapshot(verifiedShipping[0], true, "已定位 Pal/Binaries/Win64 下的实际 PalServer Shipping 进程。");

        if (verifiedShipping.Length > 1)
        {
            var launcher = candidates.SingleOrDefault(candidate =>
                configuredPath is not null
                && NormalizePath(candidate.ExecutablePath)?.Equals(configuredPath, StringComparison.OrdinalIgnoreCase) == true);
            if (launcher is not null)
            {
                var descendants = verifiedShipping
                    .Where(candidate => IsDescendantOf(candidate.ProcessId, launcher.ProcessId))
                    .ToArray();
                if (descendants.Length == 1)
                    return Snapshot(descendants[0], true, "已根据启动器父子进程关系定位实际 PalServer Shipping 进程。");
            }

            return new(null, PalServerRuntimeState.IdentityUnknown.ToString(), false,
                "发现多个属于当前安装目录的 PalServer Shipping 进程，无法确认目标实例。", null, null, null, 0, null);
        }

        var exact = candidates
            .Where(candidate => configuredPath is not null
                && NormalizePath(candidate.ExecutablePath)?.Equals(configuredPath, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        if (exact.Length == 1)
            return Snapshot(exact[0], true,
                IsShippingProcessName(exact[0].ProcessName)
                    ? "可执行文件路径与已确认的 Shipping 进程配置一致。"
                    : "尚未发现 Shipping 子进程，当前暂时定位到已确认的 PalServer 启动器。");
        if (exact.Length > 1)
            return new(null, PalServerRuntimeState.IdentityUnknown.ToString(), false,
                "发现多个与已确认 EXE 路径一致的 PalServer 进程，无法确认目标实例。", configuredPath, null, null, 0, null);

        if (candidates.Count == 1)
            return Snapshot(candidates[0], false, "发现唯一 PalServer 进程，但无法验证可执行文件路径。");

        return new(null, PalServerRuntimeState.IdentityUnknown.ToString(), false,
            $"发现 {candidates.Count} 个 PalServer 进程，无法确认目标实例。", null, null, null, 0, null);
    }

    private List<Candidate> ReadCandidates()
    {
        var candidates = new List<Candidate>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; }
                catch { continue; }
                if (!IsPalServerProcessName(name)) continue;

                string? executablePath = null;
                try { executablePath = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }

                DateTimeOffset? startedAt = null;
                try { startedAt = process.StartTime.ToUniversalTime(); } catch { }
                var threadCount = Safe(() => process.Threads.Count);
                candidates.Add(new(
                    process.Id,
                    name,
                    executablePath,
                    startedAt,
                    threadCount,
                    processTree.GetParentProcessId(process.Id)));
            }
        }
        return candidates;
    }

    private bool IsDescendantOf(int processId, int ancestorProcessId)
    {
        var current = processId;
        var visited = new HashSet<int>();
        for (var depth = 0; depth < 12; depth++)
        {
            if (!visited.Add(current)) return false;
            var parent = processTree.GetParentProcessId(current);
            if (!parent.HasValue) return false;
            if (parent.Value == ancestorProcessId) return true;
            current = parent.Value;
        }
        return false;
    }

    private static bool IsVerifiedShippingCandidate(Candidate candidate, string? serverRoot)
    {
        if (!IsShippingProcessName(candidate.ProcessName) || serverRoot is null) return false;
        var candidatePath = NormalizePath(candidate.ExecutablePath);
        if (candidatePath is null) return false;
        var expectedDirectory = NormalizePath(Path.Combine(serverRoot, "Pal", "Binaries", "Win64"));
        var actualDirectory = NormalizePath(Path.GetDirectoryName(candidatePath));
        return expectedDirectory is not null
            && actualDirectory?.Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ResolveConfiguredLauncherPath(
        PalServerRuntimeConfiguration configuration,
        string? serverRoot)
    {
        if (configuration.LaunchMode == PalServerLaunchMode.Executable)
            return NormalizePath(configuration.ExecutablePath);
        return serverRoot is null
            ? null
            : NormalizePath(Path.Combine(serverRoot, "PalServer.exe"));
    }

    private static string? ResolveConfiguredServerRoot(PalServerRuntimeConfiguration configuration)
    {
        var anchors = new List<string?>();
        if (configuration.LaunchMode == PalServerLaunchMode.Executable)
            anchors.Add(configuration.ExecutablePath);
        else
            anchors.Add(configuration.ScriptPath);
        anchors.Add(configuration.WorkingDirectory);

        foreach (var anchor in anchors)
        {
            var root = ResolveServerRootFromAnchor(anchor);
            if (root is not null) return root;
        }

        var working = NormalizePath(configuration.WorkingDirectory);
        return string.IsNullOrWhiteSpace(working) ? null : working;
    }

    private static string? ResolveServerRootFromAnchor(string? anchor)
    {
        var normalized = NormalizePath(anchor);
        if (normalized is null) return null;

        var directory = File.Exists(normalized)
            ? Path.GetDirectoryName(normalized)
            : normalized;
        if (string.IsNullOrWhiteSpace(directory)) return null;

        var current = new DirectoryInfo(directory);
        for (var depth = 0; current is not null && depth < 10; depth++, current = current.Parent)
        {
            if (current.Name.Equals("Win64", StringComparison.OrdinalIgnoreCase)
                && current.Parent?.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase) == true
                && current.Parent.Parent?.Name.Equals("Pal", StringComparison.OrdinalIgnoreCase) == true)
                return NormalizePath(current.Parent.Parent.Parent?.FullName);

            var shippingDirectory = Path.Combine(current.FullName, "Pal", "Binaries", "Win64");
            if (Directory.Exists(shippingDirectory))
                return NormalizePath(current.FullName);
        }

        return null;
    }

    private static bool IsPalServerProcessName(string name) =>
        name.Equals("PalServer", StringComparison.OrdinalIgnoreCase)
        || IsShippingProcessName(name);

    private static bool IsShippingProcessName(string name) =>
        name.Equals("PalServer-Win64-Shipping", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PalServer-Win64-Shipping-Cmd", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("PalServer-Win64-Shipping", StringComparison.OrdinalIgnoreCase);

    private static PalServerProcessSnapshot Snapshot(Candidate candidate, bool verified, string reason) =>
        new(candidate.ProcessId,
            verified ? PalServerRuntimeState.Running.ToString() : PalServerRuntimeState.IdentityUnknown.ToString(),
            verified,
            reason,
            candidate.ExecutablePath,
            candidate.StartedAt,
            candidate.StartedAt.HasValue ? DateTimeOffset.UtcNow - candidate.StartedAt.Value : null,
            candidate.ThreadCount,
            candidate.ParentProcessId);

    private static PalServerProcessSnapshot Stopped() =>
        new(null, PalServerRuntimeState.Stopped.ToString(), true, "未发现 PalServer 进程。", null, null, null, 0, null);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim())); }
        catch { return null; }
    }

    private static int Safe(Func<int> value)
    {
        try { return value(); }
        catch { return 0; }
    }

    private sealed record Candidate(
        int ProcessId,
        string ProcessName,
        string? ExecutablePath,
        DateTimeOffset? StartedAt,
        int ThreadCount,
        int? ParentProcessId);
}
