using PalOps.Web.Management;
using PalOps.Web.Rcon;
using PalOps.Web.Settings;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerShutdownService
{
    Task SafeStopAsync(
        PalServerRuntimeConfiguration configuration,
        int processId,
        Action<string, int, string> progress,
        CancellationToken cancellationToken = default);
}

public sealed class PalServerShutdownService(
    IServerSettingsStore settingsStore,
    IRconClient rcon,
    IPalServerProcessLocator locator,
    IWindowsProcessTree processTree,
    ILogger<PalServerShutdownService> logger) : IPalServerShutdownService
{
    private const int GracefulCountdownSeconds = 10;
    private const int CleanupPasses = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CleanupSettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FinalVerificationTimeout = TimeSpan.FromSeconds(10);

    public async Task SafeStopAsync(
        PalServerRuntimeConfiguration configuration,
        int processId,
        Action<string, int, string> progress,
        CancellationToken cancellationToken = default)
    {
        progress("verifying-process", 5, "正在确认待停止的 PalServer/Shipping 进程身份");
        if (!VerifyLockedProcess(configuration, processId))
        {
            progress("exit-confirmed", 100, "PalServer 已经退出");
            return;
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Rcon.Password))
            throw new PalServerRuntimeException(
                409,
                "PALSERVER_RCON_UNAVAILABLE",
                "正常停止要求配置可用的 RCON。",
                null,
                "请在系统设置中配置 RCON 后重试；程序不会在 RCON 不可用时直接终止服务器进程。");

        try
        {
            progress("sending-shutdown", 20, "正在发送 1 秒正常关服命令");
            var shutdownResult = await rcon.ExecuteAsync(
                settings.Rcon,
                "Shutdown 1 Server will shut down in 1 seconds",
                cancellationToken);
            EnsureAccepted(
                shutdownResult.Response,
                "PALSERVER_SHUTDOWN_COMMAND_FAILED",
                "服务器未确认正常关服命令，已取消后续强制清理。",
                processId);

            for (var remainingSeconds = GracefulCountdownSeconds; remainingSeconds >= 1; remainingSeconds--)
            {
                if (!LocateRemainingProcess(configuration, processId).Exists)
                {
                    progress("exit-confirmed", 100, "PalServer 已正常退出");
                    return;
                }

                var elapsedSeconds = GracefulCountdownSeconds - remainingSeconds;
                var percentage = 30 + (elapsedSeconds * 4);
                progress(
                    "waiting-graceful-exit",
                    percentage,
                    $"正常关服命令已确认，剩余 {remainingSeconds} 秒后检查并清理残留进程");
                await Task.Delay(PollInterval, cancellationToken);
            }

            if (!LocateRemainingProcess(configuration, processId).Exists)
            {
                progress("exit-confirmed", 100, "PalServer 已正常退出");
                return;
            }

            progress("forcing-engine-exit", 75, "10 秒倒计时结束，正在按已验证 PID 强制停止 PalServer 引擎");
            await TerminateLockedProcessThenResidualLauncherAsync(
                configuration,
                processId,
                progress,
                cancellationToken);

            progress("verifying-final-exit", 95, "正在确认 PalServer 引擎和启动器已完全退出");
            var finalDeadline = DateTimeOffset.UtcNow.Add(FinalVerificationTimeout);
            if (await WaitForExitAsync(configuration, processId, finalDeadline, cancellationToken))
            {
                progress("exit-confirmed", 100, "PalServer 停止流程已完成");
                return;
            }
        }
        catch (RconException ex)
        {
            throw new PalServerRuntimeException(
                409,
                "PALSERVER_RCON_UNAVAILABLE",
                "正常停止期间 RCON 通信失败，未进入进程强制清理阶段。",
                ex.Message,
                "确认 RCON 地址、端口和密码后重试正常停止。",
                ex);
        }

        var remaining = locator.LocateVerifiedProcesses(configuration);
        throw new PalServerRuntimeException(
            504,
            "PALSERVER_FORCE_STOP_TIMEOUT",
            "按 PID 终止 PalServer 并清理启动器后，仍检测到服务器进程。",
            remaining.Count == 0
                ? processId.ToString()
                : string.Join(", ", remaining.Select(item => $"{item.ProcessName}:{item.ProcessId}")),
            "请确认 PalOps 进程具备终止 PalServer 的权限，并检查是否有外部守护程序自动重启服务器。");
    }

    private bool VerifyLockedProcess(PalServerRuntimeConfiguration configuration, int processId)
    {
        var verified = locator.Locate(configuration);
        if (!verified.ProcessId.HasValue)
        {
            if (!processTree.IsProcessAlive(processId)) return false;
            throw new PalServerRuntimeException(
                409,
                "PALSERVER_IDENTITY_UNKNOWN",
                "启动操作锁定的 PalServer 进程仍在运行，但当前无法重新验证其身份，已拒绝停止。",
                processId.ToString());
        }

        if (!verified.IdentityVerified || verified.ProcessId.Value != processId)
            throw new PalServerRuntimeException(
                409,
                "PALSERVER_IDENTITY_CHANGED",
                "PalServer 进程身份在停止前发生变化，已拒绝对新的或未知进程执行停止。",
                $"expected={processId}; actual={verified.ProcessId?.ToString() ?? "none"}");

        return true;
    }

    private async Task TerminateLockedProcessThenResidualLauncherAsync(
        PalServerRuntimeConfiguration configuration,
        int lockedProcessId,
        Action<string, int, string> progress,
        CancellationToken cancellationToken)
    {
        var terminationErrors = new List<string>();
        var verifiedBeforeKill = locator.LocateVerifiedProcesses(configuration);
        if (processTree.IsProcessAlive(lockedProcessId))
        {
            if (!verifiedBeforeKill.Any(item => item.ProcessId == lockedProcessId))
                throw new PalServerRuntimeException(
                    409,
                    "PALSERVER_IDENTITY_CHANGED",
                    "倒计时结束后原 PalServer PID 仍在运行，但其身份无法再次确认，已拒绝强制终止。",
                    lockedProcessId.ToString());

            TryKill(lockedProcessId, "PalServer 引擎", terminationErrors);
            await Task.Delay(CleanupSettleDelay, cancellationToken);
        }

        for (var pass = 1; pass <= CleanupPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verified = locator.LocateVerifiedProcesses(configuration);
            if (verified.Count == 0) return;

            var launchers = verified.Where(item => item.IsLauncher).ToArray();
            if (launchers.Length > 0)
            {
                progress(
                    "cleaning-launcher",
                    80 + pass,
                    $"正在清理残留的 PalServer.exe 启动器（第 {pass}/{CleanupPasses} 次）");
                foreach (var launcher in launchers)
                    TryKill(launcher.ProcessId, "PalServer.exe 启动器", terminationErrors);
            }

            var residualEngines = locator.LocateVerifiedProcesses(configuration)
                .Where(item => !item.IsLauncher)
                .ToArray();
            if (residualEngines.Length > 0)
            {
                progress(
                    "cleaning-residual-engine",
                    86 + pass,
                    $"正在清理启动器竞态产生的残留 Shipping 进程（第 {pass}/{CleanupPasses} 次）");
                foreach (var engine in residualEngines)
                    TryKill(engine.ProcessId, "PalServer Shipping 进程", terminationErrors);
            }

            await Task.Delay(CleanupSettleDelay, cancellationToken);
        }

        var final = locator.LocateVerifiedProcesses(configuration);
        if (final.Count == 0) return;

        throw new PalServerRuntimeException(
            504,
            "PALSERVER_FORCE_STOP_TIMEOUT",
            "按 PID 终止 PalServer 并清理启动器后，仍检测到服务器进程。",
            string.Join("; ", final.Select(item => $"{item.ProcessName}:{item.ProcessId}")
                .Concat(terminationErrors)),
            "请检查 PalOps 的 Windows 进程权限，以及是否有计划任务、服务或守护脚本自动拉起 PalServer。");
    }

    private void TryKill(int processId, string role, ICollection<string> errors)
    {
        try
        {
            logger.LogWarning("Terminating verified {Role} process {ProcessId}.", role, processId);
            processTree.Kill(processId);
        }
        catch (PalServerRuntimeException ex)
        {
            logger.LogWarning(ex, "Failed to terminate verified {Role} process {ProcessId}; continuing residual cleanup.", role, processId);
            errors.Add($"{role} PID {processId}: {ex.Code} {ex.Message}");
        }
    }

    private async Task<bool> WaitForExitAsync(
        PalServerRuntimeConfiguration configuration,
        int processId,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!LocateRemainingProcess(configuration, processId).Exists) return true;
            await Task.Delay(PollInterval, cancellationToken);
        }

        return !LocateRemainingProcess(configuration, processId).Exists;
    }

    private RemainingProcess LocateRemainingProcess(
        PalServerRuntimeConfiguration configuration,
        int lockedProcessId)
    {
        if (processTree.IsProcessAlive(lockedProcessId))
            return new RemainingProcess(true, lockedProcessId);

        var located = locator.Locate(configuration);
        if (located.State == PalServerRuntimeState.Stopped.ToString())
            return new RemainingProcess(false, null);
        if (located.ProcessId.HasValue && located.IdentityVerified)
            return new RemainingProcess(true, located.ProcessId.Value);
        return new RemainingProcess(true, null);
    }

    private static void EnsureAccepted(
        string response,
        string errorCode,
        string errorMessage,
        int processId)
    {
        var interpretation = RconActionResponseInterpreter.Interpret(response);
        if (interpretation.Success) return;
        throw new PalServerRuntimeException(
            409,
            errorCode,
            errorMessage,
            $"PID={processId}; RCON={interpretation.Code}; response={response}",
            interpretation.Message);
    }

    private readonly record struct RemainingProcess(bool Exists, int? ProcessId);
}
