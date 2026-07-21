using Microsoft.AspNetCore.Mvc;
using PalOps.Web.Contracts;
using PalOps.Web.PluginManagement;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class PluginManagementEndpoints
{
    private const long MaximumRequestBytes = 270L * 1024 * 1024;

    public static IEndpointRouteBuilder MapPluginManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/plugin-management")
            .WithTags("PluginManagement")
            .RequireAuthorization();

        group.MapGet("/inventory", async (
            IPluginPackageService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.GetDashboardAsync(cancellationToken), context)));

        group.MapPost("/check-updates", async (
            IPluginPackageService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.CheckUpdatesAsync(User(context), RemoteIp(context), cancellationToken), context)))
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/install", async (
            HttpRequest request,
            IPluginPackageService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                throw new PluginManagementException(415, "PLUGIN_MULTIPART_REQUIRED", "插件安装必须使用 multipart/form-data。", suggestedAction: "选择 ZIP 文件后重新上传。");
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file")
                ?? throw new PluginManagementException(400, "PLUGIN_FILE_REQUIRED", "未收到插件 ZIP 文件。");
            var acknowledgeCompatibilityRisk = ParseBoolean(form["acknowledgeCompatibilityRisk"].ToString());
            var overwriteExisting = ParseBoolean(form["overwriteExisting"].ToString());
            await using var stream = file.OpenReadStream();
            var result = await service.InstallAsync(
                stream,
                file.FileName,
                file.Length,
                acknowledgeCompatibilityRisk,
                overwriteExisting,
                User(context),
                RemoteIp(context),
                cancellationToken);
            return Results.Ok(Response(result, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaximumRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaximumRequestBytes });

        group.MapPost("/{packageId}/toggle", async (
            string packageId,
            PluginToggleRequest request,
            IPluginPackageService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.ToggleAsync(packageId, request.Enabled, User(context), RemoteIp(context), cancellationToken), context)))
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/backups/{backupId}/rollback", async (
            string backupId,
            IPluginPackageService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await service.RollbackAsync(backupId, User(context), RemoteIp(context), cancellationToken), context)))
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context)
        => new(data, context.TraceIdentifier, []);

    private static bool ParseBoolean(string value)
        => bool.TryParse(value, out var parsed) && parsed;

    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";
    private static string RemoteIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
