using PalOps.Web.Contracts;
using PalOps.Web.Infrastructure;
using PalOps.Web.Statistics;

namespace PalOps.Web.Endpoints;

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/statistics")
            .RequireAuthorization()
            .WithTags("Statistics");

        group.MapGet("/dashboard", async (
            string? range,
            IStatisticsQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(
                await service.GetDashboardAsync(range, cancellationToken),
                context)));

        group.MapGet("/series", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? granularity,
            IStatisticsQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!from.HasValue || !to.HasValue)
                throw new PalOpsApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "STATISTICS_RANGE_REQUIRED",
                    "统计序列查询必须提供 from 和 to。");
            if (!TryParseGranularity(granularity, out var parsed))
                throw new PalOpsApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "STATISTICS_GRANULARITY_INVALID",
                    "统计粒度必须为 raw、minute、quarter 或 daily。");
            return Results.Ok(Response(
                await service.GetSeriesAsync(from.Value, to.Value, parsed, cancellationToken),
                context));
        });

        group.MapGet("/retention", async (
            IStatisticsQueryService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(
                await service.GetRetentionAsync(cancellationToken),
                context)));

        return endpoints;
    }

    private static bool TryParseGranularity(string? value, out StatisticsGranularity granularity)
    {
        granularity = value?.Trim().ToLowerInvariant() switch
        {
            "raw" or "10s" => StatisticsGranularity.Raw,
            "minute" or "1m" => StatisticsGranularity.Minute,
            "quarter" or "quarterhour" or "15m" => StatisticsGranularity.QuarterHour,
            "daily" or "1d" => StatisticsGranularity.Daily,
            _ => (StatisticsGranularity)(-1)
        };
        return Enum.IsDefined(granularity);
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context) =>
        new(data, context.TraceIdentifier, []);
}
