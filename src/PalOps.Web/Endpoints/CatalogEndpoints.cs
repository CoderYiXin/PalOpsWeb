using System.Text.Json;
using PalOps.Web.Audit;
using PalOps.Web.Catalog;
using PalOps.Web.Contracts;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/catalog").RequireAuthorization().WithTags("Catalog");

        group.MapGet("/search", async (
            string? type,
            string? q,
            string? category,
            bool favoritesOnly,
            int? offset,
            int? limit,
            ICatalogService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(type, q, category, favoritesOnly, offset ?? 0, limit ?? 100, cancellationToken);
            return Results.Ok(new CatalogSearchResponse(result.Entries.Select(ToResponse).ToArray(), result.Total));
        });

        group.MapGet("/categories", async (
            string type,
            ICatalogService service,
            CancellationToken cancellationToken) =>
        {
            var categories = await service.GetCategoriesAsync(type, cancellationToken);
            return Results.Ok(categories.Select(static item => new CatalogCategoryResponse(item.Category, item.Count)).ToArray());
        });

        group.MapPut("/{type}/{id}/favorite", async (
            string type,
            string id,
            CatalogFavoriteRequest request,
            HttpContext context,
            ICatalogService service,
            IAuditLogService audit,
            CancellationToken cancellationToken) =>
        {
            await service.SetFavoriteAsync(type, id, request.Favorite, cancellationToken);
            await audit.WriteAsync("catalog.favorite", "success", EndpointHelpers.RemoteIp(context), $"已{(request.Favorite ? "收藏" : "取消收藏")} {type}:{id}。", cancellationToken: cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/{type}/{id}/aliases", async (
            string type,
            string id,
            CatalogAliasRequest request,
            HttpContext context,
            ICatalogService service,
            IAuditLogService audit,
            CancellationToken cancellationToken) =>
        {
            if (request.Aliases is null) throw new ArgumentException("别名列表不能为空。");
            await service.SetAliasesAsync(type, id, request.Aliases, cancellationToken);
            await audit.WriteAsync("catalog.aliases", "success", EndpointHelpers.RemoteIp(context), $"已更新 {type}:{id} 的别名。", new { aliasCount = request.Aliases.Count }, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/import", async (
            IFormFile file,
            HttpContext context,
            ICatalogService service,
            IAuditLogService audit,
            CancellationToken cancellationToken) =>
        {
            if (file.Length <= 0 || file.Length > 10 * 1024 * 1024) throw new ArgumentException("目录文件必须在 1 字节到 10 MB 之间。");
            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("只支持 JSON 或 CSV 目录文件。");
            await using var stream = file.OpenReadStream();
            var result = await service.ImportAsync(file.FileName, stream, cancellationToken);
            await audit.WriteAsync("catalog.import", result.Rejected == 0 ? "success" : "partial", EndpointHelpers.RemoteIp(context), $"目录导入完成：新增 {result.Imported}，覆盖 {result.Replaced}，拒绝 {result.Rejected}。", new { fileName = Path.GetFileName(file.FileName), file.Length }, cancellationToken);
            return Results.Ok(new CatalogImportResponse(result.Imported, result.Replaced, result.Rejected, result.Errors));
        }).RequireAuthorization("Administrator").DisableAntiforgery().AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/export", async (ICatalogService service, CancellationToken cancellationToken) =>
        {
            var entries = await service.ExportOverridesAsync(cancellationToken);
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(entries, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", "palops-catalog-overrides.json");
        });

        return endpoints;
    }

    private static CatalogEntryResponse ToResponse(CatalogEntry entry)
        => new(entry.Id, entry.Type, entry.NameZh, entry.NameEn, entry.Category, entry.Aliases, entry.ImageUrl, entry.Favorite, entry.LastUsedAt, entry.Source);
}
