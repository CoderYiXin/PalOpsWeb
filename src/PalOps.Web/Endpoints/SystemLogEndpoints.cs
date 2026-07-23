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
            string? category,
            string? query,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? hasException,
            ISystemLogStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await store.ReadAsync(new SystemLogQuery(
                page ?? 1,
                pageSize ?? 100,
                level,
                category,
                query,
                from,
                to,
                hasException), cancellationToken);
            return Results.Ok(new ApiResponse<SystemLogPage>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/summary", async (
            string? level,
            string? category,
            string? query,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? hasException,
            ISystemLogStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await store.GetSummaryAsync(new SystemLogQuery(
                Level: level,
                Category: category,
                Query: query,
                From: from,
                To: to,
                HasException: hasException), cancellationToken);
            return Results.Ok(new ApiResponse<SystemLogSummary>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/export", async (
            string? format,
            string? level,
            string? category,
            string? query,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? hasException,
            ISystemLogStore store,
            CancellationToken cancellationToken) =>
        {
            var export = await store.ExportAsync(new SystemLogQuery(
                Level: level,
                Category: category,
                Query: query,
                From: from,
                To: to,
                HasException: hasException), format ?? "csv", cancellationToken);
            return Results.File(export.Content, export.ContentType, export.FileName);
        });

        group.MapDelete("/before", async (
            DateTimeOffset before,
            string confirmation,
            ISystemLogStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
                throw new ArgumentException("按日期清理系统日志必须输入 CONFIRM。");
            var removed = await store.PurgeAsync(before, cancellationToken);
            return Results.Ok(new ApiResponse<object>(new { removed, before }, context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("", async (
            string confirmation,
            ISystemLogStore store,
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
