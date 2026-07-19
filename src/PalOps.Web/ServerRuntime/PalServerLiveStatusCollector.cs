using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.External;
using PalOps.Web.Players;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerLiveStatusCollector
{
    Task<PalServerLiveStatusSnapshot> CollectAsync(
        PalServerProcessSnapshot process,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}

public sealed class PalServerLiveStatusCollector(
    IServiceScopeFactory scopeFactory,
    IOptions<AppRuntimeOptions> options,
    ILogger<PalServerLiveStatusCollector> logger) : IPalServerLiveStatusCollector
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PalServerLiveStatusSnapshot? _cached;

    public async Task<PalServerLiveStatusSnapshot> CollectAsync(
        PalServerProcessSnapshot process,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshInterval = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.LiveStatusRefreshIntervalSeconds, 5, 60));
        if (!forceRefresh && _cached is not null && now - _cached.CapturedAt < refreshInterval)
            return _cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!forceRefresh && _cached is not null && now - _cached.CapturedAt < refreshInterval)
                return _cached;

            if (!process.ProcessId.HasValue && process.State == PalServerRuntimeState.Stopped.ToString())
            {
                _cached = new(null, 0, null, null, now, "process", null);
                return _cached;
            }

            using var scope = scopeFactory.CreateScope();
            var palworldApi = scope.ServiceProvider.GetRequiredService<IPalworldApiClient>();

            var infoTask = TryGetInfoAsync(palworldApi, cancellationToken);
            var metricsTask = TryGetMetricsAsync(palworldApi, cancellationToken);
            await Task.WhenAll(infoTask, metricsTask);

            var info = infoTask.Result;
            var metrics = metricsTask.Result;
            var playerResult = new PlayerReadResult(false, 0, null);
            if (!metrics.CurrentPlayers.HasValue)
            {
                var players = scope.ServiceProvider.GetRequiredService<IPlayerAggregationService>();
                playerResult = await TryGetPlayersAsync(players, cancellationToken);
            }
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Error)) errors.Add(info.Error);
            if (!string.IsNullOrWhiteSpace(metrics.Error)) errors.Add(metrics.Error);
            if (!string.IsNullOrWhiteSpace(playerResult.Error)) errors.Add(playerResult.Error);

            var sourceParts = new List<string>();
            if (info.Success) sourceParts.Add("palworld-info");
            if (metrics.Success) sourceParts.Add("palworld-metrics");
            if (playerResult.Success) sourceParts.Add("players");

            _cached = new(
                info.ServerName,
                metrics.CurrentPlayers ?? (playerResult.Success ? playerResult.Count : null),
                metrics.MaximumPlayers,
                metrics.ServerFps,
                now,
                sourceParts.Count == 0 ? "unavailable" : string.Join('+', sourceParts),
                errors.Count == 0 ? null : string.Join(" | ", errors));
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<InfoReadResult> TryGetInfoAsync(
        IPalworldApiClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await client.GetInfoAsync(cancellationToken);
            using var document = JsonDocument.Parse(raw);
            string? serverName = null;
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("servername", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                serverName = value.GetString()?.Trim();
            }
            return new(true, serverName, null);
        }
        catch (Exception ex) when (ex is ExternalApiException or HttpRequestException or JsonException)
        {
            return new(false, null, ex.Message);
        }
    }

    private static async Task<MetricsReadResult> TryGetMetricsAsync(
        IPalworldApiClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await client.GetMetricsAsync(cancellationToken);
            return new(
                true,
                metrics.CurrentPlayers,
                metrics.MaximumPlayers,
                metrics.ServerFps,
                null);
        }
        catch (Exception ex) when (ex is ExternalApiException or HttpRequestException or JsonException)
        {
            return new(false, null, null, null, ex.Message);
        }
    }

    private static async Task<PlayerReadResult> TryGetPlayersAsync(
        IPlayerAggregationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetOnlinePlayersAsync(cancellationToken);
            return new(true, result.Count, null);
        }
        catch (Exception ex) when (ex is ExternalApiException or HttpRequestException)
        {
            return new(false, 0, ex.Message);
        }
    }

    private sealed record InfoReadResult(bool Success, string? ServerName, string? Error);
    private sealed record MetricsReadResult(
        bool Success,
        int? CurrentPlayers,
        int? MaximumPlayers,
        double? ServerFps,
        string? Error);
    private sealed record PlayerReadResult(bool Success, int Count, string? Error);
}
