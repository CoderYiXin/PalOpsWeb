using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PalOps.Web.Settings;

namespace PalOps.Web.External;

public interface IPalworldApiClient
{
    Task<IReadOnlyList<PalworldPlayer>> GetOnlinePlayersAsync(CancellationToken cancellationToken = default);
    Task<string> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<string> GetInfoAsync(PalworldConnection connection, CancellationToken cancellationToken = default);
    Task<PalworldServerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
    Task<PalworldServerMetrics> GetMetricsAsync(PalworldConnection connection, CancellationToken cancellationToken = default);
}

public sealed class PalworldApiClient(HttpClient httpClient, IServerSettingsStore settingsStore) : IPalworldApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PalworldPlayer>> GetOnlinePlayersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        using var request = CreateRequest(settings.Palworld, HttpMethod.Get, "players");
        using var response = await SendAsync(request, cancellationToken);
        var payload = await DeserializeAsync<PalworldPlayersEnvelope>(response, cancellationToken) ?? new PalworldPlayersEnvelope();
        return payload.Players.Select(static player => new PalworldPlayer(
            player.Name,
            player.AccountName,
            player.PlayerId,
            player.UserId,
            player.Ip,
            player.Ping,
            player.LocationX,
            player.LocationY,
            player.Level,
            player.BuildingCount)).ToArray();
    }

    public async Task<string> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        return await GetInfoAsync(settings.Palworld, cancellationToken);
    }

    public async Task<string> GetInfoAsync(PalworldConnection connection, CancellationToken cancellationToken = default)
    {
        ValidateConnection(connection);
        using var request = CreateRequest(connection, HttpMethod.Get, "info");
        using var response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<PalworldServerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        return await GetMetricsAsync(settings.Palworld, cancellationToken);
    }

    public async Task<PalworldServerMetrics> GetMetricsAsync(
        PalworldConnection connection,
        CancellationToken cancellationToken = default)
    {
        ValidateConnection(connection);
        using var request = CreateRequest(connection, HttpMethod.Get, "metrics");
        using var response = await SendAsync(request, cancellationToken);
        var payload = await DeserializeAsync<PalworldServerMetricsPayload>(response, cancellationToken)
            ?? new PalworldServerMetricsPayload();
        return new(
            payload.ServerFps,
            payload.CurrentPlayers,
            payload.MaximumPlayers,
            payload.ServerFrameTimeMilliseconds,
            payload.UptimeSeconds);
    }

    private static HttpRequestMessage CreateRequest(PalworldConnection settings, HttpMethod method, string relative)
    {
        var request = new HttpRequestMessage(method, ApiUriBuilder.Palworld(settings.BaseUrl, relative));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                throw new ExternalApiException(
                    "PALWORLD_API_AUTH_FAILED",
                    "Palworld REST API 认证失败（HTTP 401）。请确认用户名为 admin，并填写 PalWorldSettings.ini 中的 AdminPassword；测试会优先使用当前输入框中的密码。",
                    statusCode,
                    new { response = Limit(body) });
            }

            throw new ExternalApiException(
                "PALWORLD_API_ERROR",
                $"Palworld API 返回 HTTP {(int)statusCode}：{Limit(body)}",
                statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalApiException("PALWORLD_API_TIMEOUT", "连接 Palworld REST API 超时。");
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalApiException("PALWORLD_API_UNREACHABLE", "无法连接 Palworld REST API。", innerException: ex);
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static void ValidateConnection(PalworldConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.BaseUrl))
            throw new ExternalApiException("PALWORLD_API_NOT_CONFIGURED", "Palworld REST API 地址未配置。");
        if (string.IsNullOrWhiteSpace(connection.UserName))
            throw new ExternalApiException("PALWORLD_API_NOT_CONFIGURED", "Palworld REST API 用户名未配置。");
        if (string.IsNullOrWhiteSpace(connection.Password))
            throw new ExternalApiException("PALWORLD_API_NOT_CONFIGURED", "Palworld REST API 的 AdminPassword 未配置。");
    }

    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
}
