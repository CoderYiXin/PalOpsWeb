using PalOps.Web.Contracts;
using PalOps.Web.Security;
using PalOps.Web.Versioning;

namespace PalOps.Web.Endpoints;

public static class PlatformVersionEndpoints
{
    public static IEndpointRouteBuilder MapPlatformVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system")
            .RequireAuthorization()
            .WithTags("System");

        group.MapGet("/platform-version", async (
            IPlatformVersionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PlatformVersionStatus>(
                await service.CheckAsync(false, cancellationToken),
                context.TraceIdentifier,
                [])));

        group.MapPost("/platform-version/check", async (
            IPlatformVersionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PlatformVersionStatus>(
                await service.CheckAsync(true, cancellationToken),
                context.TraceIdentifier,
                [])))
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }
}
