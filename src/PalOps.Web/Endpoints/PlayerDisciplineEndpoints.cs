using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.PlayerDiscipline;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class PlayerDisciplineEndpoints
{
    public static IEndpointRouteBuilder MapPlayerDisciplineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/player-discipline")
            .RequireAuthorization()
            .WithTags("PlayerDiscipline");

        group.MapGet("/dashboard", async (
            IPlayerDisciplineService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.GetDashboardAsync(cancellationToken), context)));

        group.MapPost("/whitelist", async (
            WhitelistWriteRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.AddWhitelistAsync(request, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.whitelist.add", "success", EndpointHelpers.RemoteIp(context),
                $"已将 {EndpointHelpers.LimitForAudit(request.UserId)} 加入白名单。",
                new { request.UserId, user = User(context) });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/whitelist/{userId}", async (
            string userId,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.RemoveWhitelistAsync(userId, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.whitelist.remove", "success", EndpointHelpers.RemoteIp(context),
                $"已将 {EndpointHelpers.LimitForAudit(userId)} 从白名单移除。",
                new { userId, user = User(context) });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/bans", async (
            BanWriteRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.BanAsync(request, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.ban.add", "success", EndpointHelpers.RemoteIp(context),
                $"已封禁 {EndpointHelpers.LimitForAudit(request.Identifier)}。",
                new { request.Identifier, request.BanType, request.ExpiresAt, reason = EndpointHelpers.LimitForAudit(request.Reason), user = User(context) });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/bans/{identifier}/unban", async (
            string identifier,
            UnbanRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.UnbanAsync(identifier, request.Reason, User(context), cancellationToken: cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.ban.remove", "success", EndpointHelpers.RemoteIp(context),
                $"已解除 {EndpointHelpers.LimitForAudit(identifier)} 的封禁。",
                new { identifier, reason = EndpointHelpers.LimitForAudit(request.Reason), user = User(context) });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/violations", async (
            ViolationWriteRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var violation = await service.AddViolationAsync(request, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.violation.add", "success", EndpointHelpers.RemoteIp(context),
                $"已为 {EndpointHelpers.LimitForAudit(request.UserId)} 添加违规记录。",
                new { violation.Id, request.UserId, request.Severity, request.Category, user = User(context) });
            return Results.Created($"/api/v1/player-discipline/violations/{violation.Id}", Response(violation, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/identities/{userId}/notes", async (
            string userId,
            IdentityNotesWriteRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.UpdateIdentityNotesAsync(userId, request.Notes, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.identity.notes", "success", EndpointHelpers.RemoteIp(context),
                $"已更新 {EndpointHelpers.LimitForAudit(userId)} 的身份备注。",
                new { userId, user = User(context) });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/import", async (
            DisciplineImportRequest request,
            IPlayerDisciplineService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ImportAsync(request, User(context), cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "discipline.import", result.Failed == 0 ? "success" : "partial", EndpointHelpers.RemoteIp(context),
                $"玩家纪律数据导入完成：成功 {result.Imported}，失败 {result.Failed}。",
                new { request.Kind, request.Format, result.Imported, result.Failed, user = User(context) });
            return Results.Ok(Response(result, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/export", async (
            string? kind,
            string? format,
            IPlayerDisciplineService service,
            CancellationToken cancellationToken) =>
        {
            var file = await service.ExportAsync(kind ?? "whitelist", format ?? "json", cancellationToken);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return endpoints;
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context) => new(data, context.TraceIdentifier, []);
    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";
}
