using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Management;
using PalOps.Web.Infrastructure;
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

    private static bool IsAdministrator(HttpContext context)
        => context.User.IsInRole(PalOpsRoles.Owner) || context.User.IsInRole(PalOpsRoles.Administrator);

    private static string CommandName(string command)
    {
        var normalized = RconCommandNormalizer.Normalize(command);
        var separator = normalized.IndexOfAny([' ', '\t']);
        return EndpointHelpers.LimitForAudit(separator < 0 ? normalized : normalized[..separator], 80);
    }
}
