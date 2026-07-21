namespace PalOps.Web.Statistics;

public sealed class StatisticsAccumulator
{
    private readonly TimeZoneInfo _timeZone;
    private StatisticsMetricAggregate _onlinePlayers = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _serverFps = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _systemCpu = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _systemMemory = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _palServerCpu = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _palServerWorkingSet = StatisticsMetricAggregate.Empty;
    private StatisticsMetricAggregate _diskFree = StatisticsMetricAggregate.Empty;
    private StatisticsCounters _counters = StatisticsCounters.Empty;
    private DateTimeOffset? _peakOnlineAt;
    private int _sampleCount;

    public StatisticsAccumulator(
        StatisticsGranularity granularity,
        DateTimeOffset bucketStart,
        TimeZoneInfo timeZone)
    {
        Granularity = granularity;
        BucketStart = bucketStart.ToUniversalTime();
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        BucketEnd = StatisticsBucketClock.End(BucketStart, Granularity, _timeZone);
    }

    public StatisticsGranularity Granularity { get; }
    public DateTimeOffset BucketStart { get; }
    public DateTimeOffset BucketEnd { get; }

    public void RecordRuntime(StatisticsRuntimeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _sampleCount = checked(_sampleCount + 1);
        var previousPeak = _onlinePlayers.Maximum;
        _onlinePlayers = _onlinePlayers.Add(sample.OnlinePlayers);
        _serverFps = _serverFps.Add(sample.ServerFps);
        _systemCpu = _systemCpu.Add(sample.SystemCpuPercent);
        _systemMemory = _systemMemory.Add(sample.SystemMemoryPercent);
        _palServerCpu = _palServerCpu.Add(sample.PalServerCpuPercent);
        _palServerWorkingSet = _palServerWorkingSet.Add(sample.PalServerWorkingSetBytes);
        _diskFree = _diskFree.Add(sample.DiskFreeBytes);
        if (sample.OnlinePlayers.HasValue
            && (!previousPeak.HasValue || sample.OnlinePlayers.Value >= previousPeak.Value))
            _peakOnlineAt = sample.CapturedAt;
    }

    public void RecordPlayerTransitions(int joins, int leaves, IEnumerable<long> completedSessionSeconds)
    {
        ArgumentNullException.ThrowIfNull(completedSessionSeconds);
        var durations = completedSessionSeconds.Select(static value => Math.Max(0, value)).ToArray();
        _counters = _counters.AddPresence(joins, leaves, durations.Length, durations.Sum());
    }

    public void RecordEvent(string eventType) => _counters = _counters.AddEvent(eventType);

    public void Merge(StatisticsMetricPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (point.Granularity != Granularity || point.BucketStart.ToUniversalTime() != BucketStart)
            throw new ArgumentException("Cannot merge a statistics point from a different bucket.", nameof(point));
        _sampleCount = checked(_sampleCount + point.SampleCount);
        var previousPeak = _onlinePlayers.Maximum;
        _onlinePlayers = _onlinePlayers.Merge(point.OnlinePlayers);
        _serverFps = _serverFps.Merge(point.ServerFps);
        _systemCpu = _systemCpu.Merge(point.SystemCpuPercent);
        _systemMemory = _systemMemory.Merge(point.SystemMemoryPercent);
        _palServerCpu = _palServerCpu.Merge(point.PalServerCpuPercent);
        _palServerWorkingSet = _palServerWorkingSet.Merge(point.PalServerWorkingSetBytes);
        _diskFree = _diskFree.Merge(point.DiskFreeBytes);
        _counters = _counters.Merge(point.Counters);
        if (point.OnlinePlayers.Maximum.HasValue
            && (!previousPeak.HasValue || point.OnlinePlayers.Maximum.Value >= previousPeak.Value))
            _peakOnlineAt = point.PeakOnlineAt;
        else if (point.PeakOnlineAt.HasValue && !_peakOnlineAt.HasValue)
            _peakOnlineAt = point.PeakOnlineAt;
    }

    public StatisticsMetricPoint ToPoint(DateTimeOffset updatedAt) => new(
        StatisticsBucketClock.BucketId(Granularity, BucketStart),
        Granularity,
        BucketStart,
        BucketEnd,
        _sampleCount,
        _onlinePlayers,
        _serverFps,
        _systemCpu,
        _systemMemory,
        _palServerCpu,
        _palServerWorkingSet,
        _diskFree,
        _peakOnlineAt,
        _counters,
        updatedAt.ToUniversalTime());
}
