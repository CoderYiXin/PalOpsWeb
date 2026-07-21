using System.Globalization;
using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Events;
using PalOps.Web.Infrastructure;
using PalOps.Web.Management;
using PalOps.Web.PlayerDiscipline;
using PalOps.Web.Rcon;
using PalOps.Web.Security;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class ManagementEndpoints
{
    private static readonly HashSet<string> SupportedSendTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msg", "log", "ilog", "vilog"
    };

    public static IEndpointRouteBuilder MapManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/management").RequireAuthorization("Operator").WithTags("Management");

        group.MapPost("/broadcast", async (
            BroadcastRequest request,
            HttpContext context,
            IPalDefenderApiClient api,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var message = ValidateMessage(request.Message, 1000);
            await api.BroadcastAsync(message, request.Alert, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                request.Alert ? "management.alert" : "management.broadcast",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已发送{(request.Alert ? "警告" : "广播")}，长度 {message.Length}。");
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/message", async (
            PlayerMessageRequest request,
            HttpContext context,
            IPalDefenderApiClient api,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (request.PlayerIdentifiers is null) throw new ArgumentException("玩家列表不能为空。");
            var players = request.PlayerIdentifiers.Select(EndpointHelpers.ValidatePlayerIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (players.Length is < 1 or > 12) throw new ArgumentException("每次必须选择 1 到 12 名玩家。");
            var sendType = request.SendType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!SupportedSendTypes.Contains(sendType)) throw new ArgumentException("SendType 只能是 msg、log、ilog 或 vilog。");
            var message = ValidateMessage(request.Message, 1000);
            await api.SendPlayerMessageAsync(players, sendType, message, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.player-message",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已向 {players.Length} 名玩家发送 {sendType} 消息，长度 {message.Length}。",
                new { players, sendType });
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/kick", async (
            KickRequest request,
            HttpContext context,
            IPalDefenderApiClient api,
            IPlayerDisciplineService disciplineService,
            IPalOpsEventPublisher eventPublisher,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var player = EndpointHelpers.ValidatePlayerIdentifier(request.PlayerIdentifier);
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : ValidateMessage(request.Reason, 300);
            await api.KickAsync(player, reason, cancellationToken);
            try
            {
                await disciplineService.RecordKickAsync(
                    userId: player,
                    displayName: null,
                    reason: reason,
                    actor: context.User.Identity?.Name ?? "unknown",
                    source: "palops",
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("PalOps.PlayerDiscipline")
                    .LogWarning(ex, "Kick record for player {Player} could not be persisted.", player);
            }
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.kick",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已踢出玩家 {player}。",
                new { player, reasonLength = reason?.Length ?? 0 });
            try
            {
                await eventPublisher.PublishAsync(PalOpsEvent.Create(
                    "player.kicked",
                    "warning",
                    player: new Dictionary<string, object?>
                    {
                        ["identifier"] = player
                    },
                    metadata: new Dictionary<string, object?>
                    {
                        ["message"] = $"玩家 {player} 已被管理员踢出。",
                        ["reason"] = reason,
                        ["actor"] = context.User.Identity?.Name ?? "unknown",
                        ["source"] = "palops"
                    }), CancellationToken.None);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("PalOps.Notifications")
                    .LogWarning(ex, "Kick event for player {Player} could not be published.", player);
            }
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/reload-paldefender", async (
            HttpContext context,
            IPalDefenderApiClient api,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            await api.ReloadConfigAsync(cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.reload-paldefender",
                "success",
                EndpointHelpers.RemoteIp(context),
                "已请求 PalDefender 重载配置。");
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/time", async (
            TimeRequest request,
            HttpContext context,
            IServerSettingsStore settingsStore,
            IRconClient rcon,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var hour = ValidateHour(request.Hour);
            var settings = await settingsStore.GetAsync(cancellationToken);
            var result = await rcon.ExecuteAsync(settings.Rcon, $"settime {hour}", cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.set-time",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已设置服务器时间为 {hour}。",
                new { result.ElapsedMilliseconds });
            return Results.Ok(new RconExecuteResponse(true, RconRisk.Safe.ToString(), result.Response, result.ElapsedMilliseconds));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/player-position", async (
            PlayerTargetRequest request,
            HttpContext context,
            IPlayerQuickActionService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // Keep the legacy endpoint for older frontends, but route it through
            // the same aggregation-based implementation as the current quick
            // action page. This avoids issuing a PalDefender command that some
            // releases report as "Unknown command".
            var player = EndpointHelpers.ValidatePlayerIdentifier(request.PlayerIdentifier);
            var actionResult = await service.ExecuteAsync(
                new PlayerQuickActionRequest(
                    [player],
                    "get-position",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                cancellationToken);
            var result = actionResult.Results.Single();
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.player-position",
                result.Success ? "success" : "failed",
                EndpointHelpers.RemoteIp(context),
                result.Success ? $"已查询玩家 {player} 的坐标。" : $"查询玩家 {player} 坐标失败：{result.Code}。",
                new { result.Code, result.ElapsedMilliseconds });
            return Results.Ok(new RconExecuteResponse(
                result.Success,
                RconRisk.Safe.ToString(),
                string.IsNullOrWhiteSpace(result.Response) ? result.Message : result.Response,
                result.ElapsedMilliseconds));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/player-action", async (
            PlayerQuickActionRequest request,
            HttpContext context,
            IPlayerQuickActionService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if ((request.Action is "teleport-player" or "teleport-coordinates") &&
                !context.User.IsInRole(PalOpsRoles.Owner) && !context.User.IsInRole(PalOpsRoles.Administrator))
                throw new ForbiddenOperationException("玩家传送仅允许所有者或管理员执行。");
            var result = await service.ExecuteAsync(request, cancellationToken);
            var outcome = result.FailedPlayers == 0 ? "success" : result.SucceededPlayers == 0 ? "failed" : "partial";
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "management.player-action",
                outcome,
                EndpointHelpers.RemoteIp(context),
                $"玩家快捷操作 {result.Action}：成功 {result.SucceededPlayers}，失败 {result.FailedPlayers}。",
                new
                {
                    result.Action,
                    result.RequestedPlayers,
                    result.SucceededPlayers,
                    result.FailedPlayers,
                    players = result.Results.Select(static item => item.PlayerIdentifier).ToArray(),
                    codes = result.Results.Select(static item => item.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                });
            return Results.Ok(result);
        }).AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static string ValidateMessage(string value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximumLength)
            throw new ArgumentException($"消息长度必须在 1 到 {maximumLength} 个字符之间。");
        if (normalized.IndexOf('\0') >= 0) throw new ArgumentException("消息不能包含空字符。");
        return normalized;
    }

    private static string ValidateHour(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is "day" or "night") return normalized;
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var hour) || hour is < 0 or > 24)
            throw new ArgumentException("时间只能是 day、night 或 0 到 24 的数字。");
        return hour.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
