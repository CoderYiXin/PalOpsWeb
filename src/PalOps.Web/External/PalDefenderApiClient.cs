using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PalOps.Web.Settings;

namespace PalOps.Web.External;

public interface IPalDefenderApiClient
{
    Task<IReadOnlyList<PalDefenderPlayer>> GetKnownPlayersAsync(CancellationToken cancellationToken = default);
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<string> GetVersionAsync(PalDefenderConnection connection, CancellationToken cancellationToken = default);
    Task GiveItemsAsync(string playerIdentifier, IReadOnlyList<ExternalItemGrant> items, CancellationToken cancellationToken = default);
    Task GivePalsAsync(string playerIdentifier, IReadOnlyList<ExternalPalGrant> pals, CancellationToken cancellationToken = default);
    Task GiveProgressionAsync(string playerIdentifier, ExternalProgressionGrant progression, CancellationToken cancellationToken = default);
    Task LearnTechnologyAsync(string playerIdentifier, string technologyId, CancellationToken cancellationToken = default);
    Task BroadcastAsync(string message, bool alert, CancellationToken cancellationToken = default);
    Task SendPlayerMessageAsync(IReadOnlyList<string> playerIdentifiers, string sendType, string message, CancellationToken cancellationToken = default);
    Task KickAsync(string playerIdentifier, string? reason, CancellationToken cancellationToken = default);
    Task ReloadConfigAsync(CancellationToken cancellationToken = default);
}

public sealed class PalDefenderApiClient(HttpClient httpClient, IServerSettingsStore settingsStore) : IPalDefenderApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PalDefenderPlayer>> GetKnownPlayersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "players", null, cancellationToken);
        var payload = await DeserializeAsync<PalDefenderPlayersEnvelope>(response, cancellationToken) ?? new PalDefenderPlayersEnvelope();
        return payload.Players.Select(static player => new PalDefenderPlayer(
            player.Name,
            player.Ip,
            player.PlayerUid,
            player.UserId,
            player.GuildName,
            player.Status,
            player.WorldLocation?.X,
            player.WorldLocation?.Y,
            player.WorldLocation?.Z)).ToArray();
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        return await GetVersionAsync(settings.PalDefender, cancellationToken);
    }

    public async Task<string> GetVersionAsync(PalDefenderConnection connection, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(connection, HttpMethod.Get, "version", null, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task GiveItemsAsync(string playerIdentifier, IReadOnlyList<ExternalItemGrant> items, CancellationToken cancellationToken = default)
    {
        var body = new ItemsGrantPayload
        {
            Items = items.Select(static item => new ItemGrantPayload { ItemId = item.ItemId, Count = item.Count }).ToArray()
        };
        using var response = await SendAsync(HttpMethod.Post, $"give/items/{Uri.EscapeDataString(playerIdentifier)}", body, cancellationToken);
    }

    public async Task GivePalsAsync(string playerIdentifier, IReadOnlyList<ExternalPalGrant> pals, CancellationToken cancellationToken = default)
    {
        var body = new PalsGrantPayload
        {
            Pals = pals.Select(static pal => new PalGrantPayload { PalId = pal.PalId, Level = pal.Level }).ToArray()
        };
        using var response = await SendAsync(HttpMethod.Post, $"give/pals/{Uri.EscapeDataString(playerIdentifier)}", body, cancellationToken);
    }

    public async Task GiveProgressionAsync(string playerIdentifier, ExternalProgressionGrant progression, CancellationToken cancellationToken = default)
    {
        var body = new ProgressionGrantPayload
        {
            Experience = progression.Experience,
            TechnologyPoints = progression.TechnologyPoints,
            AncientTechnologyPoints = progression.AncientTechnologyPoints,
            Relics = progression.Relics
        };
        using var response = await SendAsync(HttpMethod.Post, $"give/progression/{Uri.EscapeDataString(playerIdentifier)}", body, cancellationToken);
    }

    public async Task LearnTechnologyAsync(string playerIdentifier, string technologyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(technologyId))
            throw new ArgumentException("科技 ID 不能为空。", nameof(technologyId));

        var normalized = technologyId.Trim();
        var body = new LearnTechnologyPayload
        {
            // PalDefender REST expects the case-sensitive sentinel "All".
            Technology = normalized.Equals("all", StringComparison.OrdinalIgnoreCase) ? "All" : normalized
        };
        using var response = await SendAsync(
            HttpMethod.Post,
            $"learntech/{Uri.EscapeDataString(playerIdentifier)}",
            body,
            cancellationToken);
    }

    public async Task BroadcastAsync(string message, bool alert, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, alert ? "Alert" : "Broadcast", new MessagePayload { Message = message }, cancellationToken);
    }

    public async Task SendPlayerMessageAsync(IReadOnlyList<string> playerIdentifiers, string sendType, string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIdentifiers);
        if (playerIdentifiers.Count == 0)
        {
            throw new ArgumentException("至少需要一个玩家标识。", nameof(playerIdentifiers));
        }

        var normalizedSendType = MapSendType(sendType);
        object body = playerIdentifiers.Count == 1
            ? new SinglePlayerMessagePayload
            {
                SendType = normalizedSendType,
                UserId = playerIdentifiers[0],
                Message = message
            }
            : new MultiplePlayerMessagePayload
            {
                SendType = normalizedSendType,
                UserIds = playerIdentifiers,
                Message = message
            };

        using var response = await SendAsync(HttpMethod.Post, "SendPlayerMessage", body, cancellationToken);
    }

    public async Task KickAsync(string playerIdentifier, string? reason, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"kick/{Uri.EscapeDataString(playerIdentifier)}",
            new KickPayload { Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() },
            cancellationToken);
    }

    public async Task ReloadConfigAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "ReloadConfig", new { }, cancellationToken);
    }

    private static string MapSendType(string sendType)
    {
        return sendType?.Trim().ToLowerInvariant() switch
        {
            "msg" or "playerchat" => "PlayerChat",
            "global" or "playerglobalchat" => "PlayerGlobalChat",
            "guild" or "playerguildchat" => "PlayerGuildChat",
            "log" or "playerlognormal" => "PlayerLogNormal",
            "ilog" or "playerlogimportant" => "PlayerLogImportant",
            "vilog" or "playerlogveryimportant" => "PlayerLogVeryImportant",
            _ => throw new ArgumentException("SendType 只能是 msg、global、guild、log、ilog 或 vilog。", nameof(sendType))
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        return await SendAsync(settings.PalDefender, method, relative, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        PalDefenderConnection connection,
        HttpMethod method,
        string relative,
        object? body,
        CancellationToken cancellationToken)
    {
        ValidateConnection(connection);

        using var request = new HttpRequestMessage(method, ApiUriBuilder.PalDefender(connection.BaseUrl, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var exception = await CreateExceptionAsync(response, cancellationToken);
            response.Dispose();
            throw exception;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalApiException("PALDEFENDER_API_TIMEOUT", "连接 PalDefender REST API 超时。");
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalApiException("PALDEFENDER_API_UNREACHABLE", "无法连接 PalDefender REST API。", innerException: ex);
        }
    }

    private static async Task<ExternalApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var limitedBody = Limit(body.Trim());

        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            body.Contains("invalid token", StringComparison.OrdinalIgnoreCase))
        {
            return new ExternalApiException(
                "PALDEFENDER_TOKEN_INVALID",
                "PalDefender REST API Token 无效。该 Token 不是 AdminPassword，也不是 RCON 密码；请复制 PalDefender/RESTAPI/Tokens/*.json 文件中的 Token 字段，并确认该文件不是 TokenExample.json。",
                response.StatusCode,
                string.IsNullOrWhiteSpace(limitedBody) ? null : new { response = limitedBody });
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<PalDefenderErrorEnvelope>(body, JsonOptions);
                if (envelope?.Error is { } error)
                {
                    return new ExternalApiException(error.Code, error.Message, response.StatusCode, error.Details);
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ExternalApiException(
            "PALDEFENDER_API_ERROR",
            string.IsNullOrWhiteSpace(limitedBody)
                ? $"PalDefender API 返回 HTTP {(int)response.StatusCode}。"
                : $"PalDefender API 返回 HTTP {(int)response.StatusCode}：{limitedBody}",
            response.StatusCode);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static void ValidateConnection(PalDefenderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.BaseUrl))
        {
            throw new ExternalApiException("PALDEFENDER_API_NOT_CONFIGURED", "PalDefender REST API 地址未配置。");
        }

        if (string.IsNullOrWhiteSpace(connection.Token))
        {
            throw new ExternalApiException(
                "PALDEFENDER_TOKEN_NOT_CONFIGURED",
                "PalDefender REST API Token 未配置。请从 PalDefender/RESTAPI/Tokens/*.json 中复制 Token 字段；AdminPassword 或 RCON 密码不能替代该 Token。");
        }
    }

    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
}
