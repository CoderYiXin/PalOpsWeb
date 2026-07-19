using PalOps.Web.Contracts;
using PalOps.Web.Logging;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class SystemLogEndpoints
{
    public static IEndpointRouteBuilder MapSystemLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system/logs").RequireAuthorization("Administrator").WithTags("System Logs");
        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? level,
            string? query,
            ISystemLogStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<SystemLogPage>(
                await store.ReadAsync(page ?? 1, pageSize ?? 100, level, query, cancellationToken),
                context.TraceIdentifier,
                [])));

        group.MapDelete("", async (
            string confirmation,
            ISystemLogStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
                throw new ArgumentException("清理系统日志必须输入 CONFIRM。");
            await store.ClearAsync(cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();
        return endpoints;
    }
}
