using PalOps.Web.Contracts;
using PalOps.Web.SaveGames.Diff;

namespace PalOps.Web.Endpoints;

public static class SaveDiffEndpoints
{
    private const int DefaultDetailLimit = 1_000;
    private const int MaximumExportDetails = 50_000;

    public static IEndpointRouteBuilder MapSaveDiffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/save-diffs")
            .RequireAuthorization()
            .WithTags("Save Diff");

        group.MapGet("/snapshots", async (
            ISaveDiffService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(
                await service.ListSnapshotsAsync(cancellationToken),
                context)));

        group.MapGet("/compare/latest", async (
            int? limit,
            ISaveDiffService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(
                await service.CompareLatestAsync(limit ?? DefaultDetailLimit, cancellationToken),
                context)));

        group.MapGet("/compare", async (
            string? from,
            string? to,
            int? limit,
            ISaveDiffService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(
                await service.CompareAsync(
                    from ?? string.Empty,
                    to ?? string.Empty,
                    limit ?? DefaultDetailLimit,
                    cancellationToken),
                context)));

        group.MapGet("/export", async (
            string? from,
            string? to,
            string? format,
            ISaveDiffService service,
            ISaveDiffReportWriter writer,
            CancellationToken cancellationToken) =>
        {
            var report = await service.CompareForExportAsync(
                from ?? string.Empty,
                to ?? string.Empty,
                cancellationToken);

            if (IsExportTruncated(report))
            {
                throw new SaveDiffValidationException(
                    "SAVE_DIFF_EXPORT_TOO_LARGE",
                    $"差异明细超过单分类 {MaximumExportDetails:N0} 条的导出上限，请缩小快照范围或先处理异常数据。");
            }

            var document = (format ?? "json").Trim().ToLowerInvariant() switch
            {
                "json" => await writer.WriteJsonAsync(report, cancellationToken),
                "csv" => await writer.WriteCsvAsync(report, cancellationToken),
                "markdown" or "md" => await writer.WriteMarkdownAsync(report, cancellationToken),
                _ => throw new SaveDiffValidationException(
                    "SAVE_DIFF_EXPORT_FORMAT_INVALID",
                    "导出格式必须为 json、csv 或 markdown。")
            };

            var filename = $"palops-save-diff-{SafeFilePart(report.From.SnapshotId)}-to-{SafeFilePart(report.To.SnapshotId)}.{document.Extension}";
            return Results.File(document.Content, document.ContentType, filename);
        });

        return endpoints;
    }

    private static bool IsExportTruncated(SaveDiffReport report)
        => report.PlayerChanges.Truncated
           || report.GuildChanges.Truncated
           || report.BaseChanges.Truncated
           || report.ItemChanges.Truncated
           || report.PalChanges.Truncated;

    private static string SafeFilePart(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(48)
            .ToArray());
        return safe.Length == 0 ? "snapshot" : safe;
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context)
        => new(data, context.TraceIdentifier, []);
}
