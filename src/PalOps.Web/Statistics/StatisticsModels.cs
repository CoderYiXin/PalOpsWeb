using System.Text.Json.Serialization;

namespace PalOps.Web.Statistics;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatisticsGranularity
{
    Raw,
    Minute,
    QuarterHour,
    Daily
}

public sealed record StatisticsMetricAggregate(
    long Count,
    double Sum,
    double? Minimum,
    double? Maximum,
    double? Last)
{
    public static StatisticsMetricAggregate Empty { get; } = new(0, 0, null, null, null);

    [JsonIgnore]
    public double? Average => Count > 0 ? Sum / Count : null;

    public StatisticsMetricAggregate Add(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return this;
        return new(
            Count + 1,
            Sum + value.Value,
            Minimum.HasValue ? Math.Min(Minimum.Value, value.Value) : value.Value,
            Maximum.HasValue ? Math.Max(Maximum.Value, value.Value) : value.Value,
            value.Value);
    }

    public StatisticsMetricAggregate Merge(StatisticsMetricAggregate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count == 0) return this;
        if (Count == 0) return other;
        return new(
            checked(Count + other.Count),
            Sum + other.Sum,
            Math.Min(Minimum!.Value, other.Minimum!.Value),
            Math.Max(Maximum!.Value, other.Maximum!.Value),
            other.Last ?? Last);
    }
}

public sealed record StatisticsCounters(
    int PlayerJoinCount,
    int PlayerLeaveCount,
    int CompletedSessionCount,
    long CompletedSessionSeconds,
    int ServerAbnormalCount,
    int CrashGuardRestartCount,
    int CrashGuardRecoveryCount,
    int CrashGuardFailureCount,
    int ServerStartCount,
    int ServerRestartCount,
    int ServerStopCount,
    int BackupSuccessCount,
    int BackupFailureCount,
    int WebhookSuccessCount,
    int WebhookFailureCount)
{
    public static StatisticsCounters Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public StatisticsCounters AddPresence(int joins, int leaves, int completedSessions, long completedSessionSeconds) => this with
    {
        PlayerJoinCount = checked(PlayerJoinCount + Math.Max(0, joins)),
        PlayerLeaveCount = checked(PlayerLeaveCount + Math.Max(0, leaves)),
        CompletedSessionCount = checked(CompletedSessionCount + Math.Max(0, completedSessions)),
        CompletedSessionSeconds = checked(CompletedSessionSeconds + Math.Max(0, completedSessionSeconds))
    };

    public StatisticsCounters AddEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return this;
        return eventType.Trim().ToLowerInvariant() switch
        {
            "server.exited-unexpectedly" or "server.stop-timeout" or "server.operation.failed" =>
                this with { ServerAbnormalCount = checked(ServerAbnormalCount + 1) },
            "maintenance.crash-guard.restarting" =>
                this with { CrashGuardRestartCount = checked(CrashGuardRestartCount + 1) },
            "maintenance.crash-guard.recovered" =>
                this with { CrashGuardRecoveryCount = checked(CrashGuardRecoveryCount + 1) },
            "maintenance.crash-guard.failed" =>
                this with { CrashGuardFailureCount = checked(CrashGuardFailureCount + 1) },
            "server.started" => this with { ServerStartCount = checked(ServerStartCount + 1) },
            "server.restarted" => this with { ServerRestartCount = checked(ServerRestartCount + 1) },
            "server.stopped" => this with { ServerStopCount = checked(ServerStopCount + 1) },
            "backup.completed" => this with { BackupSuccessCount = checked(BackupSuccessCount + 1) },
            "backup.failed" => this with { BackupFailureCount = checked(BackupFailureCount + 1) },
            "webhook.delivery.succeeded" => this with { WebhookSuccessCount = checked(WebhookSuccessCount + 1) },
            "webhook.delivery.failed" => this with { WebhookFailureCount = checked(WebhookFailureCount + 1) },
            _ => this
        };
    }

    public StatisticsCounters Merge(StatisticsCounters other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(
            checked(PlayerJoinCount + other.PlayerJoinCount),
            checked(PlayerLeaveCount + other.PlayerLeaveCount),
            checked(CompletedSessionCount + other.CompletedSessionCount),
            checked(CompletedSessionSeconds + other.CompletedSessionSeconds),
            checked(ServerAbnormalCount + other.ServerAbnormalCount),
            checked(CrashGuardRestartCount + other.CrashGuardRestartCount),
            checked(CrashGuardRecoveryCount + other.CrashGuardRecoveryCount),
            checked(CrashGuardFailureCount + other.CrashGuardFailureCount),
            checked(ServerStartCount + other.ServerStartCount),
            checked(ServerRestartCount + other.ServerRestartCount),
            checked(ServerStopCount + other.ServerStopCount),
            checked(BackupSuccessCount + other.BackupSuccessCount),
            checked(BackupFailureCount + other.BackupFailureCount),
            checked(WebhookSuccessCount + other.WebhookSuccessCount),
            checked(WebhookFailureCount + other.WebhookFailureCount));
    }
}

public sealed record StatisticsMetricPoint(
    string BucketId,
    StatisticsGranularity Granularity,
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    int SampleCount,
    StatisticsMetricAggregate OnlinePlayers,
    StatisticsMetricAggregate ServerFps,
    StatisticsMetricAggregate SystemCpuPercent,
    StatisticsMetricAggregate SystemMemoryPercent,
    StatisticsMetricAggregate PalServerCpuPercent,
    StatisticsMetricAggregate PalServerWorkingSetBytes,
    StatisticsMetricAggregate DiskFreeBytes,
    DateTimeOffset? PeakOnlineAt,
    StatisticsCounters Counters,
    DateTimeOffset UpdatedAt);

public sealed record StatisticsRuntimeSample(
    DateTimeOffset CapturedAt,
    int? OnlinePlayers,
    double? ServerFps,
    double? SystemCpuPercent,
    double? SystemMemoryPercent,
    double? PalServerCpuPercent,
    long? PalServerWorkingSetBytes,
    long? DiskFreeBytes);

public sealed record StatisticsActivePlayerSession(
    string PlayerKey,
    string DisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt);

public sealed record StatisticsRecorderState(
    int SchemaVersion,
    IReadOnlyList<StatisticsMetricPoint> OpenBuckets,
    IReadOnlyList<StatisticsMetricPoint> PendingPoints,
    IReadOnlyList<StatisticsActivePlayerSession> ActiveSessions,
    bool HasPresenceBaseline,
    DateTimeOffset? LastPresenceAt,
    DateTimeOffset? LastRetentionAt,
    DateTimeOffset UpdatedAt)
{
    public static StatisticsRecorderState Empty() => new(
        1,
        [],
        [],
        [],
        false,
        null,
        null,
        DateTimeOffset.UtcNow);
}

public sealed record StatisticsRetentionTierStatus(
    StatisticsGranularity Granularity,
    DateTimeOffset? Cutoff,
    DateTimeOffset? EarliestBucket,
    DateTimeOffset? LatestBucket,
    int FileCount,
    long RecordCount);

public static class StatisticsRetentionPolicy
{
    public static DateTimeOffset? Cutoff(StatisticsGranularity granularity, DateTimeOffset now) => granularity switch
    {
        StatisticsGranularity.Raw => now.AddHours(-2),
        StatisticsGranularity.Minute => now.AddDays(-7),
        StatisticsGranularity.QuarterHour => now.AddDays(-90),
        StatisticsGranularity.Daily => null,
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
    };
}

public static class StatisticsBucketClock
{
    public static DateTimeOffset Start(
        DateTimeOffset instant,
        StatisticsGranularity granularity,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var value = granularity switch
        {
            StatisticsGranularity.Raw => new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second / 10 * 10, DateTimeKind.Unspecified),
            StatisticsGranularity.Minute => new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified),
            StatisticsGranularity.QuarterHour => new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute / 15 * 15, 0, DateTimeKind.Unspecified),
            StatisticsGranularity.Daily => new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };
        return ConvertLocalToUtc(value, timeZone);
    }

    public static DateTimeOffset End(
        DateTimeOffset bucketStart,
        StatisticsGranularity granularity,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (granularity != StatisticsGranularity.Daily)
        {
            return bucketStart + (granularity switch
            {
                StatisticsGranularity.Raw => TimeSpan.FromSeconds(10),
                StatisticsGranularity.Minute => TimeSpan.FromMinutes(1),
                StatisticsGranularity.QuarterHour => TimeSpan.FromMinutes(15),
                _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
            });
        }

        var local = TimeZoneInfo.ConvertTime(bucketStart, timeZone);
        var next = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
        return ConvertLocalToUtc(next, timeZone);
    }

    public static string BucketId(StatisticsGranularity granularity, DateTimeOffset bucketStart) =>
        $"{granularity.ToString().ToLowerInvariant()}:{bucketStart.UtcDateTime:O}";

    private static DateTimeOffset ConvertLocalToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
