using PalOps.Web.Players;

namespace PalOps.Web.Endpoints;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/players/online", async (IPlayerAggregationService service, CancellationToken cancellationToken)
            => Results.Ok(await service.GetOnlinePlayersAsync(cancellationToken)))
            .RequireAuthorization()
            .WithTags("Players");
        return endpoints;
    }
}
