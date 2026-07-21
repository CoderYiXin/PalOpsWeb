using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Management;
using PalOps.Web.Infrastructure;
using PalOps.Web.PlayerDiscipline;
using PalOps.Web.Rcon;
using PalOps.Web.Security;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class RconEndpoints
{
    public static IEndpointRouteBuilder MapRconEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/rcon/execute", ExecuteAsync)
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>()
            .WithTags("RCON");

        var capabilities = endpoints.MapGroup("/api/v1/rcon/capabilities")
            .RequireAuthorization("Operator")
            .WithTags("RCON Capabilities");
        capabilities.MapGet("", (IRconCapabilityService service, HttpContext context)
            => Results.Ok(new ApiResponse<IReadOnlyList<RconCapability>>(service.GetCapabilities(), context.TraceIdentifier, [])));
        capabilities.MapPost("/probe", async (
            IRconCapabilityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<IReadOnlyList<RconCapability>>(
                await service.ProbeAsync(cancellationToken),
                context.TraceIdentifier,
                [])))
            .AddEndpointFilter<CsrfValidationFilter>();
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(
        RconExecuteRequest request,
        HttpContext context,
        IServerSettingsStore settingsStore,
        IRconClient rcon,
        IPlayerDisciplineService disciplineService,
        IAuditLogService audit,
        CancellationToken cancellationToken)
    {
        var command = RconCommandNormalizer.Normalize(request.Command ?? string.Empty);
        var risk = RconRiskClassifier.Classify(command);
        if (risk == RconRisk.High && !IsAdministrator(context))
            throw new ForbiddenOperationException("高危 RCON 指令仅允许所有者或管理员执行。");
        if (risk == RconRisk.High && !request.ConfirmHighRisk)
            throw new ArgumentException("高危 RCON 指令必须勾选风险确认后才能执行。");

        var settings = await settingsStore.GetAsync(cancellationToken);
        try
        {
            var result = await rcon.ExecuteAsync(settings.Rcon, command, cancellationToken);
            var interpretation = RconActionResponseInterpreter.Interpret(result.Response);
            await audit.WriteBestEffortAsync(
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RconAudit"),
                "rcon.execute",
                interpretation.Success ? "success" : "failed",
                EndpointHelpers.RemoteIp(context),
                $"执行 RCON：{CommandName(command)}，业务结果 {interpretation.Code}，风险等级 {risk}。",
                new
                {
                    commandName = CommandName(command),
                    risk = risk.ToString(),
                    businessCode = interpretation.Code,
                    reason = EndpointHelpers.LimitForAudit(request.Reason ?? string.Empty),
                    result.ElapsedMilliseconds,
                    responseLength = result.Response.Length
                });
            if (interpretation.Success && TryParseKickCommand(command, out var kickedUserId, out var kickReason))
            {
                try
                {
                    await disciplineService.RecordKickAsync(
                        kickedUserId,
                        displayName: null,
                        reason: kickReason,
                        actor: context.User.Identity?.Name ?? "unknown",
                        source: "rcon",
                        cancellationToken: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("PalOps.PlayerDiscipline")
                        .LogWarning(ex, "Successful RCON player.kick for {UserId} could not be persisted.", kickedUserId);
                }
            }
            return Results.Ok(new RconExecuteResponse(
                interpretation.Success,
                risk.ToString(),
                result.Response,
                result.ElapsedMilliseconds,
                interpretation.Code,
                interpretation.Message));
        }
        catch
        {
            await audit.WriteBestEffortAsync(
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RconAudit"),
                "rcon.execute",
                "failed",
                EndpointHelpers.RemoteIp(context),
                $"RCON 传输失败：{CommandName(command)}，风险等级 {risk}。",
                new { commandName = CommandName(command), risk = risk.ToString(), reason = EndpointHelpers.LimitForAudit(request.Reason ?? string.Empty) });
            throw;
        }
    }


    internal static bool TryParseKickCommand(string command, out string userId, out string? reason)
    {
        userId = string.Empty;
        reason = null;
        var normalized = RconCommandNormalizer.Normalize(command);
        var separator = normalized.IndexOfAny([' ', '\t']);
        var name = separator < 0 ? normalized : normalized[..separator];
        if (!name.Equals("kick", StringComparison.OrdinalIgnoreCase) || separator < 0) return false;

        var arguments = normalized[(separator + 1)..].Trim();
        if (arguments.Length == 0) return false;
        var identifierEnd = arguments.IndexOfAny([' ', '\t']);
        userId = identifierEnd < 0 ? arguments : arguments[..identifierEnd];
        if (identifierEnd < 0) return userId.Length > 0;

        var rawReason = arguments[(identifierEnd + 1)..].Trim();
        if (rawReason.Length >= 2 && rawReason[0] == '"' && rawReason[^1] == '"')
            rawReason = rawReason[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        reason = string.IsNullOrWhiteSpace(rawReason) ? null : rawReason;
        return userId.Length > 0;
    }

    private static bool IsAdministrator(HttpContext context)
        => context.User.IsInRole(PalOpsRoles.Owner) || context.User.IsInRole(PalOpsRoles.Administrator);

    private static string CommandName(string command)
    {
        var normalized = RconCommandNormalizer.Normalize(command);
        var separator = normalized.IndexOfAny([' ', '\t']);
        return EndpointHelpers.LimitForAudit(separator < 0 ? normalized : normalized[..separator], 80);
    }
}
