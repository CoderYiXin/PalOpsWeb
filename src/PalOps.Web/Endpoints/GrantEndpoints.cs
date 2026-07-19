using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Grants;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class GrantEndpoints
{
    public static IEndpointRouteBuilder MapGrantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/grants/bulk", async (
            BulkGrantRequest request,
            HttpContext context,
            IBulkGrantService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ExecuteAsync(request, cancellationToken);
            var outcome = response.FailedPlayers == 0 ? "success" : response.SucceededPlayers == 0 ? "failed" : "partial";
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "grant.bulk",
                outcome,
                EndpointHelpers.RemoteIp(context),
                $"向 {response.RequestedPlayers} 名在线玩家发放物资，成功 {response.SucceededPlayers}，失败 {response.FailedPlayers}。",
                new
                {
                    players = request.PlayerIdentifiers,
                    items = request.Items.Select(static item => new { item.ItemId, item.Count, item.Custom }),
                    pals = request.Pals.Select(static pal => new { pal.PalId, pal.Level, pal.Count, pal.Custom }),
                    progression = request.Progression,
                    results = response.Results
                });
            return Results.Ok(response);
        })
        .RequireAuthorization("Operator")
        .AddEndpointFilter<CsrfValidationFilter>()
        .WithTags("Grants");
        return endpoints;
    }
}
