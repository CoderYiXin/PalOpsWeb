using PalOps.Web.Contracts;
using PalOps.Web.PalworldConfiguration;
using PalOps.Web.Security;
using PalOps.Web.AdvancedOperations;
using PalOps.Web.Platform.Caching;

namespace PalOps.Web.Endpoints;

public static class PalworldConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapPalworldConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/palworld-configuration")
            .RequireAuthorization("OwnerOnly")
            .WithTags("PalworldConfiguration");

        group.MapGet("/current", async (
            IPalworldConfigurationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.GetAsync(cancellationToken), context)));

        group.MapPut("/path", async (
            PalworldConfigurationPathRequest request,
            IPalworldConfigurationService service,
            IConfigurationVersionService versions,
            IPlatformCache cache,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            _ = await versions.CaptureAutomaticAsync("palworld-path-before-change", User(context), cancellationToken);
            var result = await service.SetPathAsync(request.Path, cancellationToken);
            cache.RemoveByTag("readiness");
            return Results.Ok(Response(result, context));
        })
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/preview", async (
            PalworldConfigurationPreviewRequest request,
            IPalworldConfigurationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.PreviewAsync(request, cancellationToken), context)))
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/save", async (
            PalworldConfigurationSaveRequest request,
            IPalworldConfigurationService service,
            IConfigurationVersionService versions,
            IPlatformCache cache,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            _ = await versions.CaptureAutomaticAsync("palworld-configuration-before-save", User(context), cancellationToken);
            var result = await service.SaveAsync(request, false, User(context), EndpointHelpers.RemoteIp(context), cancellationToken);
            cache.RemoveByTag("readiness");
            return Results.Ok(Response(result, context));
        })
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/save-and-restart", async (
            PalworldConfigurationSaveRequest request,
            IPalworldConfigurationService service,
            IConfigurationVersionService versions,
            IPlatformCache cache,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            _ = await versions.CaptureAutomaticAsync("palworld-configuration-before-restart", User(context), cancellationToken);
            var result = await service.SaveAsync(request, true, User(context), EndpointHelpers.RemoteIp(context), cancellationToken);
            cache.RemoveByTag("readiness");
            return Results.Accepted(
                "/api/v1/server-runtime/status",
                Response(result, context));
        })
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context) => new(data, context.TraceIdentifier, []);
    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";
}
