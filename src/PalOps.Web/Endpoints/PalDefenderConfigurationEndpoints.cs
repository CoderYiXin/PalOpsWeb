using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.PalDefender.Configuration;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class PalDefenderConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapPalDefenderConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/paldefender/config-files")
            .RequireAuthorization()
            .WithTags("PalDefenderConfiguration");

        group.MapGet("", async (IPalDefenderConfigurationService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<IReadOnlyList<PalDefenderConfigFileSummary>>(
                await service.ListAsync(cancellationToken), context.TraceIdentifier, [])));


        group.MapGet("/metadata", (string kind, HttpContext context) =>
            Results.Ok(new ApiResponse<PalDefenderConfigMetadata>(
                PalDefenderConfigurationMetadata.Get(kind), context.TraceIdentifier, [])));

        group.MapPost("/generate", async (
            PalDefenderConfigGenerateRequest request,
            IPalDefenderConfigurationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PalDefenderGeneratedConfig>(
                await service.GenerateAsync(request.Kind, request.Name, cancellationToken), context.TraceIdentifier, [])))
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/content", async (string path, IPalDefenderConfigurationService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PalDefenderConfigFileContent>(
                await service.ReadAsync(path, cancellationToken), context.TraceIdentifier, [])));

        group.MapPost("/validate", async (PalDefenderConfigValidateRequest request, IPalDefenderConfigurationService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<PalDefenderConfigValidation>(
                await service.ValidateAsync(request.RelativePath, request.Content, cancellationToken), context.TraceIdentifier, [])))
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/content", async (
            string path,
            PalDefenderConfigWriteRequest request,
            IPalDefenderConfigurationService service,
            IAuditLogService audit,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var saved = await service.SaveAsync(path, request.Content, request.ExpectedSha256, cancellationToken);
            await audit.WriteAsync(
                "paldefender.config-save",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已保存 PalDefender 配置 {saved.File.RelativePath}。",
                new { saved.File.RelativePath, saved.File.Kind, saved.File.SizeBytes, actor = context.User.Identity?.Name ?? "unknown" },
                cancellationToken);
            return Results.Ok(new ApiResponse<PalDefenderConfigFileContent>(saved, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/content", async (
            string path,
            string? expectedSha256,
            IPalDefenderConfigurationService service,
            IAuditLogService audit,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(path, expectedSha256, cancellationToken);
            await audit.WriteAsync(
                "paldefender.config-delete",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已删除 PalDefender 配置 {path}。",
                new { relativePath = path, actor = context.User.Identity?.Name ?? "unknown" },
                cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }
}
