using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Settings;

namespace PalOps.Web.Logging;

public sealed class StartupDiagnosticsHostedService(
    IHostEnvironment environment,
    IOptions<AppRuntimeOptions> runtimeOptions,
    IServerSettingsStore settingsStore,
    IOperationalReadinessGate readinessGate,
    IHostApplicationLifetime lifetime,
    ILogger<StartupDiagnosticsHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        if (OperatingSystem.IsWindows()) Console.Title = $"PalOps Web {version} - {environment.EnvironmentName}";

        logger.LogInformation("PalOps Web {Version} 正在启动", version);
        logger.LogInformation("运行环境: {Environment}", environment.EnvironmentName);
        logger.LogInformation("内容目录: {ContentRoot}", environment.ContentRootPath);
        logger.LogInformation("数据目录: {DataDirectory}", runtimeOptions.Value.DataDirectory);
        logger.LogInformation("存档目录: {WorldDirectory}", string.IsNullOrWhiteSpace(settings.SaveGame.WorldDirectory) ? "未配置" : settings.SaveGame.WorldDirectory);

        var operational = await readinessGate.GetSnapshotAsync(cancellationToken);
        if (!operational.HasAnyOperationalConfiguration)
        {
            logger.LogInformation("首次系统配置尚未完成：Palworld REST、RCON、PalDefender 本地接口、存档索引、备份、巡检、统计与业务自动任务保持暂停；PalOps/PalDefender 的 GitHub 远端版本检查仍保持可用。保存对应系统设置后将自动激活，无需重启 PalOps。");
        }
        else
        {
            logger.LogInformation("已启用的运行能力: {Capabilities}", operational.Available);
        }

        lifetime.ApplicationStarted.Register(() => logger.LogInformation("PalOps 启动完成，监听地址由 ASP.NET Core Hosting 日志列出。"));
        lifetime.ApplicationStopping.Register(() => logger.LogInformation("PalOps 正在停止。"));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
