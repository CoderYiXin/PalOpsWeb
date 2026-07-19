using PalOps.Web.Contracts;
using PalOps.Web.Security;
using PalOps.Web.Versioning;

namespace PalOps.Web.Endpoints;

public static class PalDefenderVersionEndpoints
{
    public static IEndpointRouteBuilder MapPalDefenderVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/paldefender/version")
            .RequireAuthorization()
            .WithTags("PalDefenderVersion");

        group.MapGet("", async (
            IPalDefenderVersionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PalDefenderVersionStatus>(
                await service.CheckAsync(false, cancellationToken), context.TraceIdentifier, [])));

        group.MapPost("/check", async (
            IPalDefenderVersionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PalDefenderVersionStatus>(
                await service.CheckAsync(true, cancellationToken), context.TraceIdentifier, [])))
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }
}
