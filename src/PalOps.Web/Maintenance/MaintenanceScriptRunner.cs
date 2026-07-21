using System.Diagnostics;
using System.Text;

namespace PalOps.Web.Maintenance;

public sealed record MaintenanceScriptResult(int ExitCode, long DurationMs, string StandardOutput, string StandardError);

public interface IMaintenanceScriptRunner
{
    Task<MaintenanceScriptResult> RunAsync(
        string scriptPath,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}

public sealed class MaintenanceScriptRunner(ILogger<MaintenanceScriptRunner> logger) : IMaintenanceScriptRunner
{
    private const int MaximumCapturedCharacters = 32_000;
    private static readonly HashSet<string> AllowedScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cmd", ".bat", ".ps1", ".exe"
    };

    public async Task<MaintenanceScriptResult> RunAsync(
        string scriptPath,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("维护脚本仅支持 Windows 本机部署。");

        var fullPath = ValidateAndResolve(scriptPath);
        var startInfo = BuildStartInfo(fullPath, arguments ?? string.Empty);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException("维护脚本进程启动失败。");

        var outputTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var errorTask = ReadBoundedAsync(process.StandardError, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 7200)));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAfterTerminationAsync(process, outputTask, errorTask);
            throw new TimeoutException($"维护脚本执行超过 {timeoutSeconds} 秒，已终止进程树。");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAfterTerminationAsync(process, outputTask, errorTask);
            throw;
        }

        var standardOutput = await outputTask;
        var standardError = await errorTask;
        stopwatch.Stop();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"维护脚本返回退出代码 {process.ExitCode}。{FormatError(standardError, standardOutput)}");

        return new MaintenanceScriptResult(
            process.ExitCode,
            stopwatch.ElapsedMilliseconds,
            standardOutput,
            standardError);
    }

    private static string ValidateAndResolve(string rawPath)
    {
        var value = (rawPath ?? string.Empty).Trim();
        if (value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("维护脚本不允许位于网络共享路径。");

        if (!Path.IsPathFullyQualified(value))
            throw new InvalidOperationException("维护脚本必须使用绝对本地路径。");

        var fullPath = Path.GetFullPath(value);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root) && new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidOperationException("维护脚本不允许位于映射的网络驱动器。");
        if (!AllowedScriptExtensions.Contains(Path.GetExtension(fullPath)))
            throw new InvalidOperationException("维护脚本仅支持 .cmd、.bat、.ps1 或 .exe。");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("维护脚本不存在。", fullPath);
        EnsureNoReparsePoint(new FileInfo(fullPath));
        return fullPath;
    }

    private static void EnsureNoReparsePoint(FileSystemInfo item)
    {
        FileSystemInfo? current = item;
        while (current is not null)
        {
            current.Refresh();
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"维护脚本路径包含符号链接或重解析点：{current.FullName}");
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fullPath, string arguments)
    {
        var extension = Path.GetExtension(fullPath);
        var workingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            info.Arguments = $"/d /s /c \"\"{fullPath}\" {arguments}\"";
        }
        else if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "powershell.exe";
            info.Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{fullPath}\" {arguments}";
        }
        else
        {
            info.FileName = fullPath;
            info.Arguments = arguments;
        }

        return info;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var builder = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) break;

            var remaining = MaximumCapturedCharacters - builder.Length;
            if (remaining > 0) builder.Append(buffer, 0, Math.Min(count, remaining));
        }
        return builder.ToString().Trim();
    }

    private async Task DrainAfterTerminationAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask)
    {
        try
        {
            using var terminationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (!process.HasExited) await process.WaitForExitAsync(terminationTimeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Maintenance script did not confirm termination within the drain grace period.");
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Maintenance script redirected output could not be fully drained after termination.");
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to terminate maintenance script process {ProcessId}.", process.Id);
        }
    }

    private static string FormatError(string standardError, string standardOutput)
    {
        var text = !string.IsNullOrWhiteSpace(standardError) ? standardError : standardOutput;
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return " " + (text.Length <= 500 ? text : text[..500]);
    }
}
