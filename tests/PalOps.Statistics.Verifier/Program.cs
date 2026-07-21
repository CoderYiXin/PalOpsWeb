using Microsoft.Extensions.Logging.Abstractions;
using PalOps.Web.Infrastructure;
using PalOps.Web.Statistics;

VerifyBucketBoundaries();
VerifyNullableAggregation();
VerifyCountersAndSessions();
VerifyMerge();
VerifyRetentionCutoffs();
await VerifyRepositoryOrderingAndRetentionAsync();
Console.WriteLine("PalOps statistics verifier passed.");

static void VerifyBucketBoundaries()
{
    var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+09", TimeSpan.FromHours(9), "UTC+09", "UTC+09");
    var instant = DateTimeOffset.Parse("2026-07-20T16:23:47+09:00");
    Assert(StatisticsBucketClock.Start(instant, StatisticsGranularity.Raw, zone) == DateTimeOffset.Parse("2026-07-20T07:23:40Z"), "raw bucket must floor to ten seconds");
    Assert(StatisticsBucketClock.Start(instant, StatisticsGranularity.Minute, zone) == DateTimeOffset.Parse("2026-07-20T07:23:00Z"), "minute bucket must floor to the minute");
    Assert(StatisticsBucketClock.Start(instant, StatisticsGranularity.QuarterHour, zone) == DateTimeOffset.Parse("2026-07-20T07:15:00Z"), "quarter bucket must floor to fifteen minutes");
    Assert(StatisticsBucketClock.Start(instant, StatisticsGranularity.Daily, zone) == DateTimeOffset.Parse("2026-07-19T15:00:00Z"), "daily bucket must use local midnight");
}

static void VerifyNullableAggregation()
{
    var start = DateTimeOffset.Parse("2026-07-20T07:00:00Z");
    var accumulator = new StatisticsAccumulator(StatisticsGranularity.Minute, start, TimeZoneInfo.Utc);
    accumulator.RecordRuntime(new StatisticsRuntimeSample(start.AddSeconds(5), 3, null, 20, 40, null, 1024, 5000));
    accumulator.RecordRuntime(new StatisticsRuntimeSample(start.AddSeconds(15), 5, 55, 40, 60, 10, 3072, 3000));
    var point = accumulator.ToPoint(start.AddMinutes(1));
    Assert(point.OnlinePlayers.Count == 2 && Math.Abs(point.OnlinePlayers.Average!.Value - 4) < 0.001, "online average must use both samples");
    Assert(point.ServerFps.Count == 1 && Math.Abs(point.ServerFps.Average!.Value - 55) < 0.001, "missing FPS must not be treated as zero");
    Assert(point.DiskFreeBytes.Minimum == 3000, "disk minimum must be retained");
    Assert(point.PeakOnlineAt == start.AddSeconds(15), "peak time must follow the maximum online sample");
}

static void VerifyCountersAndSessions()
{
    var start = DateTimeOffset.Parse("2026-07-20T07:00:00Z");
    var accumulator = new StatisticsAccumulator(StatisticsGranularity.Daily, start, TimeZoneInfo.Utc);
    accumulator.RecordPlayerTransitions(2, 1, new[] { 120L, 300L });
    accumulator.RecordEvent("server.exited-unexpectedly");
    accumulator.RecordEvent("server.stop-timeout");
    accumulator.RecordEvent("maintenance.crash-guard.restarting");
    accumulator.RecordEvent("maintenance.crash-guard.recovered");
    accumulator.RecordEvent("maintenance.crash-guard.failed");
    accumulator.RecordEvent("backup.completed");
    accumulator.RecordEvent("backup.failed");
    accumulator.RecordEvent("webhook.delivery.succeeded");
    accumulator.RecordEvent("webhook.delivery.retrying");
    var point = accumulator.ToPoint(start.AddDays(1));
    Assert(point.Counters.PlayerJoinCount == 2 && point.Counters.PlayerLeaveCount == 1, "presence counters must be recorded");
    Assert(point.Counters.CompletedSessionCount == 2 && point.Counters.CompletedSessionSeconds == 420, "session durations must be accumulated");
    Assert(point.Counters.ServerAbnormalCount == 2, "unexpected exits and stop timeouts must count as abnormalities");
    Assert(point.Counters.CrashGuardRestartCount == 1, "automatic restart attempts must be counted");
    Assert(point.Counters.CrashGuardRecoveryCount == 1, "automatic recovery must be counted");
    Assert(point.Counters.CrashGuardFailureCount == 1, "automatic recovery failures must be counted");
    Assert(point.Counters.BackupSuccessCount == 1 && point.Counters.BackupFailureCount == 1, "backup outcomes must be counted");
    Assert(point.Counters.WebhookSuccessCount == 1 && point.Counters.WebhookFailureCount == 0, "retrying deliveries must not enter final outcome totals");
}

static void VerifyMerge()
{
    var start = DateTimeOffset.Parse("2026-07-20T07:00:00Z");
    var first = new StatisticsAccumulator(StatisticsGranularity.Minute, start, TimeZoneInfo.Utc);
    first.RecordRuntime(new StatisticsRuntimeSample(start.AddSeconds(5), 2, 50, 10, 20, 5, 1000, 9000));
    var second = new StatisticsAccumulator(StatisticsGranularity.Minute, start, TimeZoneInfo.Utc);
    second.Merge(first.ToPoint(start.AddSeconds(10)));
    second.RecordRuntime(new StatisticsRuntimeSample(start.AddSeconds(15), 8, 60, 30, 40, 15, 3000, 7000));
    var point = second.ToPoint(start.AddMinutes(1));
    Assert(point.OnlinePlayers.Count == 2 && point.OnlinePlayers.Maximum == 8, "restored accumulator must merge metric state");
    Assert(point.SampleCount == 2, "restored accumulator must preserve sample count");
}

static void VerifyRetentionCutoffs()
{
    var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
    Assert(StatisticsRetentionPolicy.Cutoff(StatisticsGranularity.Raw, now) == now.AddHours(-2), "raw retention must be two hours");
    Assert(StatisticsRetentionPolicy.Cutoff(StatisticsGranularity.Minute, now) == now.AddDays(-7), "minute retention must be seven days");
    Assert(StatisticsRetentionPolicy.Cutoff(StatisticsGranularity.QuarterHour, now) == now.AddDays(-90), "quarter retention must be ninety days");
    Assert(StatisticsRetentionPolicy.Cutoff(StatisticsGranularity.Daily, now) is null, "daily retention must be unlimited");
}


static async Task VerifyRepositoryOrderingAndRetentionAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "palops-statistics-verifier-" + Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonlStatisticsRepository(
            new TestRuntimePathResolver(root),
            NullLogger<JsonlStatisticsRepository>.Instance);
        var minuteStart = DateTimeOffset.Parse("2026-07-20T07:00:00Z");
        await repository.AppendAsync(Point(StatisticsGranularity.Minute, minuteStart, 5, minuteStart.AddMinutes(2)));
        await repository.AppendAsync(Point(StatisticsGranularity.Minute, minuteStart, 1, minuteStart.AddMinutes(1)));
        var latest = await repository.QueryAsync(
            StatisticsGranularity.Minute,
            minuteStart.AddMinutes(-1),
            minuteStart.AddMinutes(2),
            10);
        Assert(latest.Count == 1 && latest[0].SampleCount == 5, "duplicate buckets must retain the newest UpdatedAt value");

        var now = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var expiredStart = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var retainedStart = DateTimeOffset.Parse("2026-04-22T09:00:00Z");
        await repository.AppendAsync(Point(StatisticsGranularity.QuarterHour, expiredStart, 1, expiredStart.AddMinutes(1)));
        await repository.AppendAsync(Point(StatisticsGranularity.QuarterHour, retainedStart, 1, retainedStart.AddMinutes(1)));
        await repository.ApplyRetentionAsync(now);
        var retained = await repository.QueryAsync(
            StatisticsGranularity.QuarterHour,
            expiredStart.AddDays(-1),
            now,
            10);
        Assert(retained.Count == 1 && retained[0].BucketStart == retainedStart, "boundary shards must be compacted per record");
    }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (Exception) { }
    }
}

static StatisticsMetricPoint Point(
    StatisticsGranularity granularity,
    DateTimeOffset start,
    int sampleCount,
    DateTimeOffset updatedAt)
{
    var aggregate = new StatisticsMetricAggregate(sampleCount, sampleCount, 1, 1, 1);
    return new(
        StatisticsBucketClock.BucketId(granularity, start),
        granularity,
        start,
        StatisticsBucketClock.End(start, granularity, TimeZoneInfo.Utc),
        sampleCount,
        aggregate,
        aggregate,
        aggregate,
        aggregate,
        aggregate,
        aggregate,
        aggregate,
        start,
        StatisticsCounters.Empty,
        updatedAt);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}


file sealed class TestRuntimePathResolver(string root) : IRuntimePathResolver
{
    public string DataDirectory { get; } = Path.GetFullPath(root);

    public string ResolveDataPath(params string[] segments) =>
        Path.GetFullPath(segments.Aggregate(DataDirectory, Path.Combine));

    public string ResolveConfiguredDirectory(string configured, string fallbackSubdirectory) =>
        throw new NotSupportedException();
}
