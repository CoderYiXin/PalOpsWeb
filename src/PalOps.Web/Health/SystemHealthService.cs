using System.Diagnostics;
using PalOps.Web.Automation;
using PalOps.Web.Backups;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Infrastructure;
using PalOps.Web.Logging;
using PalOps.Web.Notifications;
using PalOps.Web.Platform.Caching;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Tasks;
using PalOps.Web.Platform.Workers;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.Settings;

namespace PalOps.Web.Health;

public interface ISystemHealthService
{
    IReadOnlyList<HealthComponentV1> Components { get; }
    SystemHealthDashboardV1 Dashboard { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemHealthService(
    IServiceScopeFactory scopeFactory,
    INotificationAlertPolicyService alerts,
    IPlatformTaskCoordinator taskCenter,
    IPlatformCache cache,
    IBackgroundWorkerSupervisor workers,
    ISystemLogStore logs,
    IRuntimePathResolver paths,
    ILogger<SystemHealthService> logger,
    IOperationalReadinessGate readinessGate) : BackgroundService, ISystemHealthService
{
    private IReadOnlyList<HealthComponentV1> _components = [];
    private SystemHealthDashboardV1 _dashboard = EmptyDashboard();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public IReadOnlyList<HealthComponentV1> Components => Volatile.Read(ref _components);
    public SystemHealthDashboardV1 Dashboard => Volatile.Read(ref _dashboard);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        workers.RunAsync("system-health", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            workers.Heartbeat("system-health");
            try { await RefreshAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "System health refresh failed."); }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settingsStore = scope.ServiceProvider.GetRequiredService<IServerSettingsStore>();
            var settings = await settingsStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var operational = await readinessGate.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var checkedAt = DateTimeOffset.UtcNow;
            var results = new List<HealthComponentV1>();

            results.Add(operational.HasAny(OperationalCapability.PalworldRest)
                ? await CheckAsync("palworldRest", checkedAt, async token =>
                {
                    var client = scope.ServiceProvider.GetRequiredService<IPalworldApiClient>();
                    _ = await client.GetInfoAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("palworldRest", checkedAt, "Palworld REST API 未配置，自动健康检查保持暂停。"));

            results.Add(operational.HasAny(OperationalCapability.PalDefender)
                ? await CheckAsync("palDefenderRest", checkedAt, async token =>
                {
                    var client = scope.ServiceProvider.GetRequiredService<IPalDefenderApiClient>();
                    _ = await client.GetVersionAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("palDefenderRest", checkedAt, "PalDefender 未配置，自动健康检查保持暂停。"));

            results.Add(operational.HasAny(OperationalCapability.Rcon)
                ? await CheckAsync("rcon", checkedAt, async token =>
                {
                    var client = scope.ServiceProvider.GetRequiredService<IRconClient>();
                    var response = await client.ExecuteAsync(settings.Rcon, "Info", token).ConfigureAwait(false);
                    if (response.Response.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("RCON Info 返回 Unknown command。");
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("rcon", checkedAt, "RCON 未配置，自动健康检查保持暂停。"));

            results.Add(operational.HasAny(OperationalCapability.SaveDirectory)
                ? await CheckAsync("saveIndex", checkedAt, async token =>
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ISaveIndexRepository>();
                    _ = await repository.GetCurrentAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("saveIndex", checkedAt, "世界存档目录未配置，存档索引检查保持暂停。"));

            results.Add(operational.HasAny(OperationalCapability.BackupDirectory)
                ? await CheckAsync("backups", checkedAt, async token =>
                {
                    var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
                    _ = await service.GetSummaryAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("backups", checkedAt, "备份目录未配置，备份检查保持暂停。"));

            results.Add(operational.HasAny(OperationalCapability.Automation)
                ? await CheckAsync("automation", checkedAt, async token =>
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
                    _ = await repository.ListJobsAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false)
                : NotConfiguredComponent("automation", checkedAt, "自动化调度未启用，调度器保持暂停。"));

            var taskSnapshot = await taskCenter.GetDashboardAsync(500, cancellationToken).ConfigureAwait(false);
            var cacheSnapshot = cache.GetSnapshot();
            var workerSnapshot = workers.GetSnapshots();
            var logSnapshot = logs.GetTelemetry();
            var host = ReadHostRuntime(paths.DataDirectory);

            results.Add(new HealthComponentV1(
                "taskCenter",
                taskSnapshot.FailedLast24Hours > 0 ? "degraded" : "healthy",
                null,
                checkedAt,
                taskSnapshot.FailedLast24Hours > 0 ? $"最近 24 小时有 {taskSnapshot.FailedLast24Hours} 个失败或超时任务。" : null));
            results.Add(new HealthComponentV1(
                "backgroundWorkers",
                workerSnapshot.Any(static item => item.IsStale || item.Status == "failed") ? "degraded" : "healthy",
                null,
                checkedAt,
                workerSnapshot.Any(static item => item.IsStale) ? "存在心跳过期的后台工作器。" : null));
            results.Add(new HealthComponentV1(
                "platformCache",
                "healthy",
                null,
                checkedAt,
                $"命中率 {cacheSnapshot.HitRate:F2}%，当前 {cacheSnapshot.EntryCount} 项。"));
            results.Add(new HealthComponentV1(
                "systemLogging",
                logSnapshot.DroppedEntries > 0 ? "degraded" : "healthy",
                null,
                checkedAt,
                logSnapshot.DroppedEntries > 0 ? $"日志队列累计丢弃 {logSnapshot.DroppedEntries} 条记录。" : null));
            results.Add(new HealthComponentV1(
                "hostStorage",
                host.DiskFreePercent < 5 ? "unavailable" : host.DiskFreePercent < 15 ? "degraded" : "healthy",
                null,
                checkedAt,
                $"数据盘剩余 {host.DiskFreePercent:F1}%。"));

            var dashboard = BuildDashboard(results, host, taskSnapshot, cacheSnapshot, workerSnapshot, logSnapshot, checkedAt);
            Volatile.Write(ref _components, results);
            Volatile.Write(ref _dashboard, dashboard);
            if (operational.HasAnyOperationalConfiguration)
                await alerts.ObserveHealthAsync(results, cancellationToken).ConfigureAwait(false);
        }
        finally { _refreshGate.Release(); }
    }

    private static SystemHealthDashboardV1 BuildDashboard(
        IReadOnlyList<HealthComponentV1> components,
        HostRuntimeHealthV1 host,
        PlatformTaskDashboard tasks,
        PlatformCacheSnapshot cache,
        IReadOnlyList<BackgroundWorkerSnapshot> workers,
        SystemLogTelemetry logs,
        DateTimeOffset generatedAt)
    {
        var unavailable = components.Count(static item => item.Status == "unavailable");
        var degraded = components.Count(static item => item.Status == "degraded");
        var notConfigured = components.Count(static item => item.Status == "notConfigured");
        var score = Math.Clamp(100 - unavailable * 18 - degraded * 8 - notConfigured * 3, 0, 100);
        var overall = unavailable > 0 ? "unavailable" : degraded > 0 ? "degraded" : "healthy";
        var categories = new[]
        {
            Category("connectivity", components, "palworldRest", "palDefenderRest", "rcon"),
            Category("data", components, "saveIndex", "backups", "hostStorage"),
            Category("automation", components, "automation", "taskCenter", "backgroundWorkers"),
            Category("platform", components, "platformCache", "systemLogging")
        };
        var remediations = components.Where(static item => item.Status is "degraded" or "unavailable" or "notConfigured")
            .Select(item => item.Message ?? $"请检查 {item.Name}。")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        return new(
            overall,
            score,
            generatedAt,
            components,
            categories,
            host,
            new(tasks.Queued, tasks.Running, tasks.FailedLast24Hours, tasks.CompletedLast24Hours),
            new(cache.Hits, cache.Misses, cache.HitRate, cache.EntryCount, cache.Evictions),
            new(workers.Count, workers.Count(static item => item.Status == "running"), workers.Count(static item => item.Status == "failed"), workers.Count(static item => item.IsStale), workers.Sum(static item => item.RestartCount)),
            new(logs.WrittenEntries, logs.DroppedEntries, logs.FileCount, logs.TotalSizeBytes, logs.LastWriteAt),
            remediations);
    }

    private static HealthCategorySummaryV1 Category(string name, IReadOnlyList<HealthComponentV1> components, params string[] names)
    {
        var selected = components.Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var status = selected.Any(static item => item.Status == "unavailable") ? "unavailable"
            : selected.Any(static item => item.Status == "degraded") ? "degraded"
            : selected.Any(static item => item.Status == "notConfigured") ? "partial"
            : "healthy";
        return new(name, status, selected.Count(static item => item.Status == "healthy"), selected.Length);
    }

    private static HostRuntimeHealthV1 ReadHostRuntime(string dataDirectory)
    {
        using var process = Process.GetCurrentProcess();
        var root = Path.GetPathRoot(Path.GetFullPath(dataDirectory)) ?? Path.DirectorySeparatorChar.ToString();
        var drive = new DriveInfo(root);
        var total = drive.IsReady ? drive.TotalSize : 0;
        var free = drive.IsReady ? drive.AvailableFreeSpace : 0;
        return new(
            process.WorkingSet64,
            GC.GetTotalMemory(false),
            process.Threads.Count,
            free,
            total,
            total <= 0 ? 0 : Math.Round(free * 100d / total, 2),
            Math.Max(0, (long)(DateTimeOffset.UtcNow - new DateTimeOffset(process.StartTime.ToUniversalTime())).TotalSeconds));
    }


    private static HealthComponentV1 NotConfiguredComponent(
        string name,
        DateTimeOffset checkedAt,
        string message) =>
        new(name, "notConfigured", 0, checkedAt, message);

    private static async Task<HealthComponentV1> CheckAsync(
        string name,
        DateTimeOffset checkedAt,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await action(timeout.Token).ConfigureAwait(false);
            return new(name, "healthy", stopwatch.ElapsedMilliseconds, checkedAt, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var status = ex is ExternalApiException or RconException or OperationCanceledException ? "unavailable" : "degraded";
            return new(name, status, stopwatch.ElapsedMilliseconds, checkedAt, Limit(ex.Message));
        }
    }

    private static SystemHealthDashboardV1 EmptyDashboard() => new(
        "unknown", 0, DateTimeOffset.MinValue, [], [], new(0, 0, 0, 0, 0, 0, 0),
        new(0, 0, 0, 0), new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0), new(0, 0, 0, 0, null), []);
    private static string Limit(string value) => value.Length <= 300 ? value : value[..300];
}
