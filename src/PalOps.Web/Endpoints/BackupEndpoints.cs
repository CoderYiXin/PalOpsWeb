using PalOps.Web.Audit;
using PalOps.Web.Backups;
using PalOps.Web.Contracts;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/backups").RequireAuthorization("Operator").WithTags("Backups");

        group.MapGet("", async (IBackupService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<IReadOnlyList<BackupRecordV1>>(
                await service.ListAsync(cancellationToken), context.TraceIdentifier, [])));

        group.MapGet("/summary", async (IBackupService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<BackupSummaryV1>(
                await service.GetSummaryAsync(cancellationToken), context.TraceIdentifier, [])));

        group.MapPost("", async (
            CreateBackupRequest request,
            IBackupService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var record = await service.CreateAsync(request.Note, request.ExecuteSaveFirst, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "backup.create",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已创建世界存档备份 {record.FileName}。",
                new { record.Id, record.FileName, record.SizeBytes, record.Sha256, record.FileCount });
            return Results.Created($"/api/v1/backups/{record.Id}", new ApiResponse<BackupRecordV1>(record, context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/{id}/verify", async (
            string id,
            IBackupService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyAsync(id, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "backup.verify",
                result.Valid ? "success" : "failed",
                EndpointHelpers.RemoteIp(context),
                result.Valid ? $"备份 {id} 校验通过。" : $"备份 {id} 校验失败。",
                result);
            return Results.Ok(new ApiResponse<BackupVerificationV1>(result, context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/{id}/download", async (string id, IBackupService service, CancellationToken cancellationToken) =>
        {
            var path = await service.GetArchivePathAsync(id, cancellationToken);
            return Results.File(path, "application/zip", Path.GetFileName(path), enableRangeProcessing: true);
        });

        group.MapGet("/{id}/restore-preflight", async (
            string id,
            IBackupService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<BackupRestorePreflightV1>(
                await service.GetRestorePreflightAsync(id, cancellationToken), context.TraceIdentifier, [])))
            .RequireAuthorization("Administrator");

        group.MapPost("/{id}/restore", async (
            string id,
            RestoreBackupRequest request,
            IBackupService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.RestoreAsync(id, request.Confirmation, request.BackupName, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "backup.restore",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已从备份 {request.BackupName} 恢复世界存档。",
                new { id, request.BackupName });
            return Results.Ok(new ApiResponse<object>(new { success = true }, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/{id}", async (
            string id,
            string confirmation,
            IBackupService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
                throw new ArgumentException("删除备份必须输入 CONFIRM。");
            await service.DeleteAsync(id, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "backup.delete",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已删除备份 {id}。",
                new { id });
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }
}
