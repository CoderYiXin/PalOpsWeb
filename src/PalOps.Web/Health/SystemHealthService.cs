using System.Diagnostics;
using PalOps.Web.Automation;
using PalOps.Web.Backups;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.Settings;
using PalOps.Web.Notifications;

namespace PalOps.Web.Health;

public interface ISystemHealthService
{
    IReadOnlyList<HealthComponentV1> Components { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemHealthService(
    IServiceScopeFactory scopeFactory,
    INotificationAlertPolicyService alerts,
    ILogger<SystemHealthService> logger) : BackgroundService, ISystemHealthService
{
    private IReadOnlyList<HealthComponentV1> _components = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public IReadOnlyList<HealthComponentV1> Components => Volatile.Read(ref _components);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RefreshAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "System health refresh failed."); }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settingsStore = scope.ServiceProvider.GetRequiredService<IServerSettingsStore>();
            var settings = await settingsStore.GetAsync(cancellationToken);
            var checkedAt = DateTimeOffset.UtcNow;
            var results = new List<HealthComponentV1>();

            results.Add(await CheckAsync("palworldRest", checkedAt, async token =>
            {
                var client = scope.ServiceProvider.GetRequiredService<IPalworldApiClient>();
                _ = await client.GetInfoAsync(token);
            }, cancellationToken));

            results.Add(await CheckAsync("palDefenderRest", checkedAt, async token =>
            {
                var client = scope.ServiceProvider.GetRequiredService<IPalDefenderApiClient>();
                _ = await client.GetVersionAsync(token);
            }, cancellationToken));

            results.Add(await CheckAsync("rcon", checkedAt, async token =>
            {
                var client = scope.ServiceProvider.GetRequiredService<IRconClient>();
                var response = await client.ExecuteAsync(settings.Rcon, "Info", token);
                if (response.Response.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("RCON Info 返回 Unknown command。");
            }, cancellationToken));

            results.Add(await CheckAsync("saveIndex", checkedAt, async token =>
            {
                if (string.IsNullOrWhiteSpace(settings.SaveGame.WorldDirectory))
                    throw new NotConfiguredHealthException("未配置世界存档目录。");
                var repository = scope.ServiceProvider.GetRequiredService<ISaveIndexRepository>();
                _ = await repository.GetCurrentAsync(token);
            }, cancellationToken));

            results.Add(await CheckAsync("backups", checkedAt, async token =>
            {
                var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
                _ = await service.GetSummaryAsync(token);
            }, cancellationToken));

            results.Add(await CheckAsync("automation", checkedAt, async token =>
            {
                var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
                _ = await repository.ListJobsAsync(token);
            }, cancellationToken));

            Volatile.Write(ref _components, results);
            await alerts.ObserveHealthAsync(results, cancellationToken);
        }
        finally { _refreshGate.Release(); }
    }

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
            await action(timeout.Token);
            stopwatch.Stop();
            return new HealthComponentV1(name, "healthy", stopwatch.ElapsedMilliseconds, checkedAt, null);
        }
        catch (NotConfiguredHealthException ex)
        {
            stopwatch.Stop();
            return new HealthComponentV1(name, "notConfigured", stopwatch.ElapsedMilliseconds, checkedAt, ex.Message);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var status = ex is ExternalApiException or RconException or OperationCanceledException ? "unavailable" : "degraded";
            return new HealthComponentV1(name, status, stopwatch.ElapsedMilliseconds, checkedAt, Limit(ex.Message));
        }
    }

    private static string Limit(string value) => value.Length <= 300 ? value : value[..300];
    private sealed class NotConfiguredHealthException(string message) : Exception(message);
}
