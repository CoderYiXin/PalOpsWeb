using PalOps.Web.Contracts;
using PalOps.Web.Infrastructure;
using PalOps.Web.Platform.Tasks;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class TaskCenterEndpoints
{
    public static IEndpointRouteBuilder MapTaskCenterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/task-center")
            .RequireAuthorization()
            .WithTags("Task Center");

        group.MapGet("", async (
            int? limit,
            IPlatformTaskCoordinator coordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var dashboard = await coordinator.GetDashboardAsync(limit ?? 200, cancellationToken);
            return Results.Ok(new ApiResponse<PlatformTaskDashboard>(dashboard, context.TraceIdentifier, []));
        });

        group.MapGet("/{id}", async (
            string id,
            IPlatformTaskCoordinator coordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var task = await coordinator.FindAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("任务记录不存在。");
            return Results.Ok(new ApiResponse<PlatformTaskRecord>(task, context.TraceIdentifier, []));
        });

        group.MapPost("/{id}/cancel", async (
            string id,
            IPlatformTaskCoordinator coordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!await coordinator.CancelAsync(id, cancellationToken))
                throw new InvalidOperationException("任务未运行、已结束或不可取消。");
            var task = await coordinator.FindAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("任务记录不存在。");
            return Results.Accepted($"/api/v1/task-center/{id}", new ApiResponse<PlatformTaskRecord>(task, context.TraceIdentifier, []));
        })
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/{id}/retry", async (
            string id,
            IPlatformTaskCoordinator coordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var task = await coordinator.RetryAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("任务尚未结束，或当前进程中已不存在可重试的执行器。");
            return Results.Accepted($"/api/v1/task-center/{id}", new ApiResponse<PlatformTaskRecord>(task, context.TraceIdentifier, []));
        })
            .RequireAuthorization("Operator")
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }
}
