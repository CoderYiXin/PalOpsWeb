using PalOps.Web.Contracts;
using PalOps.Web.Security;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Endpoints;

public static class ServerRuntimeEndpoints
{
    public static IEndpointRouteBuilder MapServerRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/server-runtime")
            .RequireAuthorization()
            .WithTags("ServerRuntime");

        group.MapGet("/status", (IPalServerRuntimeCoordinator service, HttpContext context) =>
            Results.Ok(Response(service.Current, context)));

        group.MapPost("/refresh", async (
            IPalServerRuntimeCoordinator service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.RefreshAsync(true, cancellationToken), context)))
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/discover", async (
            IPalServerDiscoveryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.DiscoverAsync(cancellationToken), context)))
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/configuration", async (
            IPalServerRuntimeConfigurationStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await store.GetAsync(cancellationToken), context)))
            .RequireAuthorization("OwnerOnly");

        group.MapPut("/configuration", async (
            PalServerRuntimeConfigurationWriteRequest request,
            IPalServerRuntimeConfigurationStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<PalServerLaunchMode>(request.LaunchMode, true, out var launchMode))
                throw new ArgumentException("LaunchMode 必须为 Script 或 Executable。");
            var configuration = new PalServerRuntimeConfiguration(
                1,
                true,
                launchMode,
                request.ExecutablePath,
                request.ScriptPath,
                request.WorkingDirectory,
                request.Arguments,
                request.StartupTimeoutSeconds,
                request.ShutdownTimeoutSeconds,
                request.SaveWaitSeconds,
                request.RestartCooldownSeconds,
                DateTimeOffset.UtcNow,
                User(context));
            var saved = await store.SaveAsync(configuration, cancellationToken);
            return Results.Ok(Response(saved, context));
        })
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/start", async (
            IPalServerRuntimeCoordinator service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Accepted(await service.StartAsync(User(context), EndpointHelpers.RemoteIp(context), cancellationToken), context))
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/stop", async (
            IPalServerRuntimeCoordinator service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Accepted(await service.StopAsync(User(context), EndpointHelpers.RemoteIp(context), cancellationToken), context))
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/restart", async (
            IPalServerRuntimeCoordinator service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Accepted(await service.RestartAsync(User(context), EndpointHelpers.RemoteIp(context), cancellationToken), context))
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/force-stop", async (
            ForceStopRequest request,
            IPalServerRuntimeCoordinator service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Accepted(await service.ForceStopAsync(
                User(context),
                EndpointHelpers.RemoteIp(context),
                request.Confirmation,
                request.Reason,
                cancellationToken), context))
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/operations", async (
            int? limit,
            IServerOperationHistoryStore history,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response<IReadOnlyList<ServerOperationHistoryRecord>>(
                await history.ListAsync(limit ?? 100, cancellationToken), context)))
            .RequireAuthorization("Auditor");

        group.MapGet("/operations/{operationId}", (
            string operationId,
            IPalServerRuntimeCoordinator service,
            HttpContext context) =>
        {
            var operation = service.FindOperation(operationId)
                ?? throw new KeyNotFoundException("未找到指定的服务器操作。");
            return Results.Ok(Response(operation, context));
        });

        return endpoints;
    }

    private static IResult Accepted(ServerOperationSnapshot operation, HttpContext context) =>
        Results.Accepted(
            $"/api/v1/server-runtime/operations/{operation.OperationId}",
            Response(operation, context));

    private static ApiResponse<T> Response<T>(T data, HttpContext context) =>
        new(data, context.TraceIdentifier, []);

    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";
}
