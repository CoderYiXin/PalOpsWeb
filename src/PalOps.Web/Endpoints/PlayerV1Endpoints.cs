using PalOps.Web.Contracts;
using PalOps.Web.Players;

namespace PalOps.Web.Endpoints;

public static class PlayerV1Endpoints
{
    public static IEndpointRouteBuilder MapPlayerV1Endpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/players").RequireAuthorization().WithTags("Players v1");

        group.MapGet("", async (
            string? query,
            string? status,
            string? guildId,
            int? minLevel,
            int? maxLevel,
            string? sort,
            int? page,
            int? pageSize,
            IPlayerIndexQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await service.GetPlayersAsync(
                new PlayerListQuery(query, status, guildId, minLevel, maxLevel, sort, page ?? 1, pageSize ?? 50),
                cancellationToken);
            return Results.Ok(new ApiResponse<PagedData<PlayerListItemV1>>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/{playerUid}", async (
            string playerUid,
            IPlayerIndexQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PlayerDetailV1>(
                await service.GetPlayerAsync(playerUid, cancellationToken),
                context.TraceIdentifier,
                [])));

        group.MapGet("/{playerUid}/items", async (
            string playerUid,
            string? query,
            string? containerType,
            int? quality,
            bool? recognized,
            int? page,
            int? pageSize,
            IPlayerIndexQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await service.GetItemsAsync(
                playerUid,
                new PlayerItemQuery(query, containerType, quality, recognized, page ?? 1, pageSize ?? 50),
                cancellationToken);
            return Results.Ok(new ApiResponse<PagedData<PlayerItemV1>>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/{playerUid}/pals", async (
            string playerUid,
            string? query,
            string? gender,
            string? containerType,
            bool? isBoss,
            bool? isLucky,
            bool? isTower,
            string? passiveId,
            string? skillId,
            int? minLevel,
            int? maxLevel,
            int? page,
            int? pageSize,
            IPlayerIndexQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await service.GetPalsAsync(
                playerUid,
                new PlayerPalQuery(
                    query,
                    gender,
                    containerType,
                    isBoss,
                    isLucky,
                    isTower,
                    passiveId,
                    skillId,
                    minLevel,
                    maxLevel,
                    page ?? 1,
                    pageSize ?? 50),
                cancellationToken);
            return Results.Ok(new ApiResponse<PagedData<PlayerPalV1>>(data, context.TraceIdentifier, []));
        });

        return endpoints;
    }
}
