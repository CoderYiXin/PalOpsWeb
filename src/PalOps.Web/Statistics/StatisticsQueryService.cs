using PalOps.Web.Infrastructure;

namespace PalOps.Web.Statistics;

public sealed record StatisticsSeriesPoint(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    int SampleCount,
    double? AverageOnlinePlayers,
    double? MaximumOnlinePlayers,
    DateTimeOffset? PeakOnlineAt,
    double? AverageServerFps,
    double? MinimumServerFps,
    double? MaximumServerFps,
    double? AverageSystemCpuPercent,
    double? MaximumSystemCpuPercent,
    double? AverageSystemMemoryPercent,
    double? MaximumSystemMemoryPercent,
    double? AveragePalServerCpuPercent,
    double? MaximumPalServerCpuPercent,
    double? AveragePalServerWorkingSetBytes,
    double? MaximumPalServerWorkingSetBytes,
    double? MinimumDiskFreeBytes,
    double? MaximumDiskFreeBytes,
    StatisticsCounters Counters);

public sealed record StatisticsDashboardSummary(
    double? AverageOnlinePlayers,
    double? PeakOnlinePlayers,
    DateTimeOffset? PeakOnlineAt,
    double? AverageServerFps,
    int ServerAbnormalCount,
    int CrashGuardRestartCount,
    int CrashGuardRecoveryCount,
    int CrashGuardFailureCount,
    int ServerStartCount,
    int ServerRestartCount,
    int ServerStopCount,
    int PlayerJoinCount,
    int PlayerLeaveCount,
    double? AverageSessionSeconds,
    double? BackupSuccessRate,
    double? WebhookSuccessRate);

public sealed record StatisticsDailySummary(
    string Date,
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    double? AverageOnlinePlayers,
    double? PeakOnlinePlayers,
    DateTimeOffset? PeakOnlineAt,
    double? AverageServerFps,
    int PlayerJoinCount,
    int PlayerLeaveCount,
    double? AverageSessionSeconds,
    int ServerAbnormalCount,
    int CrashGuardRestartCount,
    int CrashGuardRecoveryCount,
    int CrashGuardFailureCount,
    int ServerRestartCount,
    int ServerStopCount,
    double? BackupSuccessRate,
    double? WebhookSuccessRate);

public sealed record StatisticsDashboard(
    string Range,
    StatisticsGranularity Granularity,
    DateTimeOffset From,
    DateTimeOffset To,
    StatisticsDashboardSummary Summary,
    IReadOnlyList<StatisticsSeriesPoint> Series,
    IReadOnlyList<StatisticsDailySummary> DailyRows,
    IReadOnlyList<StatisticsRetentionTierStatus> Retention,
    DateTimeOffset GeneratedAt);

public interface IStatisticsQueryService
{
    Task<StatisticsDashboard> GetDashboardAsync(string? range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsSeriesPoint>> GetSeriesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsGranularity granularity,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsRetentionTierStatus>> GetRetentionAsync(CancellationToken cancellationToken = default);
}

public sealed class StatisticsQueryService(
    IStatisticsRepository repository,
    IStatisticsRecorder recorder,
    TimeProvider timeProvider) : IStatisticsQueryService
{
    private const int MaximumPoints = 10_000;
    private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.Local;

    public async Task<StatisticsDashboard> GetDashboardAsync(
        string? range,
        CancellationToken cancellationToken = default)
    {
        var definition = DashboardRange.Parse(range);
        var now = timeProvider.GetUtcNow();
        var from = now - definition.Duration;
        var points = await LoadPointsAsync(definition.Granularity, from, now, cancellationToken);
        var daily = await LoadPointsAsync(StatisticsGranularity.Daily, from, now, cancellationToken);
        var retention = await repository.GetRetentionStatusAsync(now, cancellationToken);
        return new(
            definition.Name,
            definition.Granularity,
            from,
            now,
            BuildSummary(points),
            points.Select(ToSeriesPoint).ToArray(),
            daily.Select(ToDailySummary).ToArray(),
            retention,
            now);
    }

    public async Task<IReadOnlyList<StatisticsSeriesPoint>> GetSeriesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsGranularity granularity,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        var points = await LoadPointsAsync(granularity, from, to, cancellationToken);
        return points.Select(ToSeriesPoint).ToArray();
    }

    public Task<IReadOnlyList<StatisticsRetentionTierStatus>> GetRetentionAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetRetentionStatusAsync(timeProvider.GetUtcNow(), cancellationToken);

    private async Task<IReadOnlyList<StatisticsMetricPoint>> LoadPointsAsync(
        StatisticsGranularity granularity,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        ValidateRange(from, to);
        IReadOnlyList<StatisticsMetricPoint> persisted;
        try
        {
            persisted = await repository.QueryAsync(granularity, from, to, MaximumPoints, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new PalOpsApiException(
                StatusCodes.Status422UnprocessableEntity,
                "STATISTICS_POINT_LIMIT_EXCEEDED",
                $"统计查询最多返回 {MaximumPoints:N0} 个时间点，请缩小查询范围或使用更粗的聚合层级。",
                null,
                null,
                ex);
        }
        var open = await recorder.GetOpenPointsAsync(cancellationToken);
        var result = persisted
            .Concat(open.Where(point => point.Granularity == granularity
                                        && point.BucketEnd > from
                                        && point.BucketStart < to))
            .GroupBy(static point => point.BucketId, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(point => point.UpdatedAt).Last())
            .OrderBy(static point => point.BucketStart)
            .ToArray();
        if (result.Length > MaximumPoints)
            throw new PalOpsApiException(
                StatusCodes.Status422UnprocessableEntity,
                "STATISTICS_POINT_LIMIT_EXCEEDED",
                $"统计查询最多返回 {MaximumPoints:N0} 个时间点，请缩小查询范围或使用更粗的聚合层级。");
        return result;
    }

    private static void ValidateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            throw new PalOpsApiException(
                StatusCodes.Status422UnprocessableEntity,
                "STATISTICS_RANGE_INVALID",
                "统计查询结束时间必须晚于开始时间。");
        if (to - from > MaximumRange)
            throw new PalOpsApiException(
                StatusCodes.Status422UnprocessableEntity,
                "STATISTICS_RANGE_TOO_LARGE",
                "统计查询范围不能超过 366 天。");
    }

    private static StatisticsDashboardSummary BuildSummary(IReadOnlyList<StatisticsMetricPoint> points)
    {
        var aggregate = Merge(points);
        var counters = aggregate.Counters;
        return new(
            aggregate.OnlinePlayers.Average,
            aggregate.OnlinePlayers.Maximum,
            aggregate.PeakOnlineAt,
            aggregate.ServerFps.Average,
            counters.ServerAbnormalCount,
            counters.CrashGuardRestartCount,
            counters.CrashGuardRecoveryCount,
            counters.CrashGuardFailureCount,
            counters.ServerStartCount,
            counters.ServerRestartCount,
            counters.ServerStopCount,
            counters.PlayerJoinCount,
            counters.PlayerLeaveCount,
            counters.CompletedSessionCount > 0
                ? counters.CompletedSessionSeconds / (double)counters.CompletedSessionCount
                : null,
            SuccessRate(counters.BackupSuccessCount, counters.BackupFailureCount),
            SuccessRate(counters.WebhookSuccessCount, counters.WebhookFailureCount));
    }

    private StatisticsDailySummary ToDailySummary(StatisticsMetricPoint point)
    {
        var local = TimeZoneInfo.ConvertTime(point.BucketStart, _timeZone);
        var counters = point.Counters;
        return new(
            local.ToString("yyyy-MM-dd"),
            point.BucketStart,
            point.BucketEnd,
            point.OnlinePlayers.Average,
            point.OnlinePlayers.Maximum,
            point.PeakOnlineAt,
            point.ServerFps.Average,
            counters.PlayerJoinCount,
            counters.PlayerLeaveCount,
            counters.CompletedSessionCount > 0
                ? counters.CompletedSessionSeconds / (double)counters.CompletedSessionCount
                : null,
            counters.ServerAbnormalCount,
            counters.CrashGuardRestartCount,
            counters.CrashGuardRecoveryCount,
            counters.CrashGuardFailureCount,
            counters.ServerRestartCount,
            counters.ServerStopCount,
            SuccessRate(counters.BackupSuccessCount, counters.BackupFailureCount),
            SuccessRate(counters.WebhookSuccessCount, counters.WebhookFailureCount));
    }

    private static StatisticsSeriesPoint ToSeriesPoint(StatisticsMetricPoint point) => new(
        point.BucketStart,
        point.BucketEnd,
        point.SampleCount,
        point.OnlinePlayers.Average,
        point.OnlinePlayers.Maximum,
        point.PeakOnlineAt,
        point.ServerFps.Average,
        point.ServerFps.Minimum,
        point.ServerFps.Maximum,
        point.SystemCpuPercent.Average,
        point.SystemCpuPercent.Maximum,
        point.SystemMemoryPercent.Average,
        point.SystemMemoryPercent.Maximum,
        point.PalServerCpuPercent.Average,
        point.PalServerCpuPercent.Maximum,
        point.PalServerWorkingSetBytes.Average,
        point.PalServerWorkingSetBytes.Maximum,
        point.DiskFreeBytes.Minimum,
        point.DiskFreeBytes.Maximum,
        point.Counters);

    private static StatisticsMetricPoint Merge(IReadOnlyList<StatisticsMetricPoint> points)
    {
        if (points.Count == 0)
        {
            return new(
                "summary:empty",
                StatisticsGranularity.Daily,
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                0,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                StatisticsMetricAggregate.Empty,
                null,
                StatisticsCounters.Empty,
                DateTimeOffset.UtcNow);
        }

        var sampleCount = 0;
        var online = StatisticsMetricAggregate.Empty;
        var fps = StatisticsMetricAggregate.Empty;
        var systemCpu = StatisticsMetricAggregate.Empty;
        var systemMemory = StatisticsMetricAggregate.Empty;
        var palCpu = StatisticsMetricAggregate.Empty;
        var palMemory = StatisticsMetricAggregate.Empty;
        var disk = StatisticsMetricAggregate.Empty;
        var counters = StatisticsCounters.Empty;
        DateTimeOffset? peakAt = null;
        double? peakValue = null;
        foreach (var point in points)
        {
            sampleCount = checked(sampleCount + point.SampleCount);
            online = online.Merge(point.OnlinePlayers);
            fps = fps.Merge(point.ServerFps);
            systemCpu = systemCpu.Merge(point.SystemCpuPercent);
            systemMemory = systemMemory.Merge(point.SystemMemoryPercent);
            palCpu = palCpu.Merge(point.PalServerCpuPercent);
            palMemory = palMemory.Merge(point.PalServerWorkingSetBytes);
            disk = disk.Merge(point.DiskFreeBytes);
            counters = counters.Merge(point.Counters);
            if (point.OnlinePlayers.Maximum.HasValue
                && (!peakValue.HasValue || point.OnlinePlayers.Maximum.Value >= peakValue.Value))
            {
                peakValue = point.OnlinePlayers.Maximum.Value;
                peakAt = point.PeakOnlineAt;
            }
        }

        return new(
            "summary",
            points[0].Granularity,
            points.Min(static point => point.BucketStart),
            points.Max(static point => point.BucketEnd),
            sampleCount,
            online,
            fps,
            systemCpu,
            systemMemory,
            palCpu,
            palMemory,
            disk,
            peakAt,
            counters,
            points.Max(static point => point.UpdatedAt));
    }

    private static double? SuccessRate(int success, int failure)
    {
        var total = success + failure;
        return total > 0 ? success * 100d / total : null;
    }

    private sealed record DashboardRange(
        string Name,
        TimeSpan Duration,
        StatisticsGranularity Granularity)
    {
        public static DashboardRange Parse(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "24h" => new("24h", TimeSpan.FromHours(24), StatisticsGranularity.Minute),
            "7d" => new("7d", TimeSpan.FromDays(7), StatisticsGranularity.QuarterHour),
            "30d" => new("30d", TimeSpan.FromDays(30), StatisticsGranularity.QuarterHour),
            "90d" => new("90d", TimeSpan.FromDays(90), StatisticsGranularity.Daily),
            _ => throw new PalOpsApiException(
                StatusCodes.Status422UnprocessableEntity,
                "STATISTICS_RANGE_INVALID",
                "统计范围必须为 24h、7d、30d 或 90d。")
        };
    }
}
