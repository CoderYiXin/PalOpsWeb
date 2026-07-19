using System.Diagnostics;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerProcessController
{
    Task<PalServerProcessSnapshot> StartAsync(PalServerRuntimeConfiguration configuration, CancellationToken cancellationToken = default);
    Task ForceStopAsync(PalServerProcessSnapshot process, CancellationToken cancellationToken = default);
}

public sealed class PalServerProcessController(IPalServerProcessLocator locator, IWindowsProcessTree processTree) : IPalServerProcessController
{
    public async Task<PalServerProcessSnapshot> StartAsync(PalServerRuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PalServerRuntimeException(409, "PALSERVER_WINDOWS_REQUIRED", "进程管理仅支持 Windows 本机部署。");

        var existing = locator.Locate(configuration);
        if (existing.ProcessId.HasValue || existing.State == PalServerRuntimeState.IdentityUnknown.ToString())
            throw new PalServerRuntimeException(409,
                existing.IdentityVerified ? "PALSERVER_ALREADY_RUNNING" : "PALSERVER_IDENTITY_UNKNOWN",
                existing.IdentityReason);

        var startInfo = BuildStartInfo(configuration);
        try
        {
            using var launched = Process.Start(startInfo)
                ?? throw new PalServerRuntimeException(500, "PALSERVER_START_FAILED", "Windows 未能创建服务器进程。");
        }
        catch (PalServerRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new PalServerRuntimeException(500, "PALSERVER_START_FAILED", "Windows 未能启动 PalServer。", ex.Message, null, ex);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(configuration.StartupTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = locator.Locate(configuration);
            if (found.ProcessId.HasValue && found.IdentityVerified) return found;
            if (found.State == PalServerRuntimeState.IdentityUnknown.ToString())
                throw new PalServerRuntimeException(409, "PALSERVER_IDENTITY_UNKNOWN", found.IdentityReason);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var launchTarget = configuration.LaunchMode == PalServerLaunchMode.Script
            ? configuration.ScriptPath
            : configuration.ExecutablePath;
        throw new PalServerRuntimeException(504, "PALSERVER_START_TIMEOUT",
            "PalServer 未在启动超时内进入可识别状态。", launchTarget,
            "请检查启动脚本、工作目录、启动参数和 PalServer 日志。服务器进程可能仍在运行，请先确认后再重试。");
    }

    public Task ForceStopAsync(PalServerProcessSnapshot process, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.ProcessId.HasValue || !process.IdentityVerified)
            throw new PalServerRuntimeException(409, "PALSERVER_IDENTITY_UNKNOWN", "无法验证目标进程，禁止强制停止。", process.IdentityReason);
        processTree.Kill(process.ProcessId.Value);
        return Task.CompletedTask;
    }

    private static ProcessStartInfo BuildStartInfo(PalServerRuntimeConfiguration configuration)
    {
        ProcessStartInfo info;
        if (configuration.LaunchMode == PalServerLaunchMode.Script)
        {
            var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandProcessor))
            {
                var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                commandProcessor = string.IsNullOrWhiteSpace(systemDirectory)
                    ? "cmd.exe"
                    : Path.Combine(systemDirectory, "cmd.exe");
            }

            info = new ProcessStartInfo(commandProcessor);
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(BuildScriptCommand(configuration.ScriptPath, configuration.Arguments));
        }
        else
        {
            info = new ProcessStartInfo(configuration.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(configuration.Arguments))
                info.Arguments = configuration.Arguments;
        }

        info.WorkingDirectory = configuration.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return info;
    }

    private static string BuildScriptCommand(string scriptPath, string arguments)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", "CMD 模式未配置启动脚本。");

        var normalizedPath = Path.GetFullPath(scriptPath.Trim());
        var command = $"call \"{normalizedPath}\"";
        return string.IsNullOrWhiteSpace(arguments)
            ? command
            : command + " " + arguments.Trim();
    }
}
