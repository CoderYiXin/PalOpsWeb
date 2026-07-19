using PalOps.Web.Audit;

namespace PalOps.Web.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", async (
            int? page,
            int? pageSize,
            string? eventType,
            IAuditLogService audit,
            CancellationToken cancellationToken)
            => Results.Ok(await audit.ReadAsync(page ?? 1, pageSize ?? 50, eventType, cancellationToken)))
            .RequireAuthorization("Auditor")
            .WithTags("Audit");
        return endpoints;
    }
}
