using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Statistics;

public interface IStatisticsRecorder
{
    Task RecordRuntimeAsync(
        PalServerRuntimeSnapshot snapshot,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);
    Task RecordPlayerPresenceAsync(
        IReadOnlyCollection<PlayerResponse> players,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);
    Task RecordEventAsync(PalOpsEvent palOpsEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsMetricPoint>> GetOpenPointsAsync(CancellationToken cancellationToken = default);
}

public sealed class StatisticsRecorder(
    IStatisticsRepository repository,
    IStatisticsStateStore stateStore,
    ILogger<StatisticsRecorder> logger) : IStatisticsRecorder
{
    private static readonly StatisticsGranularity[] Granularities = Enum.GetValues<StatisticsGranularity>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.Local;
    private readonly Dictionary<StatisticsGranularity, StatisticsAccumulator> _open = [];
    private readonly List<StatisticsMetricPoint> _pending = [];
    private readonly Dictionary<string, StatisticsActivePlayerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;
    private bool _hasPresenceBaseline;
    private bool _needsPresenceReconciliation;
    private DateTimeOffset? _lastPresenceAt;
    private DateTimeOffset? _lastRetentionAt;

    public Task RecordRuntimeAsync(
        PalServerRuntimeSnapshot snapshot,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default) =>
        ExecuteBestEffortAsync(async () =>
        {
            var sample = new StatisticsRuntimeSample(
                capturedAt,
                snapshot.LiveStatus?.OnlinePlayers,
                snapshot.LiveStatus?.ServerFps,
                snapshot.Metrics?.SystemCpuPercent,
                snapshot.Metrics?.SystemMemoryPercent,
                snapshot.Metrics?.PalServerCpuPercent,
                snapshot.Metrics?.PalServerWorkingSetBytes,
                snapshot.Metrics?.DiskFreeBytes);
            foreach (var accumulator in EnsureBuckets(capturedAt)) accumulator.RecordRuntime(sample);
            await FlushAndPersistAsync(capturedAt, cancellationToken);
        }, "runtime sample", cancellationToken);

    public Task RecordPlayerPresenceAsync(
        IReadOnlyCollection<PlayerResponse> players,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default) =>
        ExecuteBestEffortAsync(async () =>
        {
            var current = players
                .Where(static player => player.Online)
                .GroupBy(PlayerKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            if (!_hasPresenceBaseline)
            {
                foreach (var entry in current)
                    _sessions[entry.Key] = Session(entry.Key, entry.Value, capturedAt);
                _hasPresenceBaseline = true;
                _needsPresenceReconciliation = false;
                _lastPresenceAt = capturedAt;
                await FlushAndPersistAsync(capturedAt, cancellationToken);
                return;
            }

            var joins = 0;
            var leaves = 0;
            var durations = new List<long>();
            foreach (var entry in current)
            {
                if (_sessions.TryGetValue(entry.Key, out var existing))
                {
                    _sessions[entry.Key] = existing with
                    {
                        DisplayName = DisplayName(entry.Value),
                        LastSeenAt = capturedAt
                    };
                }
                else
                {
                    _sessions[entry.Key] = Session(entry.Key, entry.Value, capturedAt);
                    if (!_needsPresenceReconciliation) joins++;
                }
            }

            foreach (var missing in _sessions.Keys.Where(key => !current.ContainsKey(key)).ToArray())
            {
                var session = _sessions[missing];
                var endedAt = _needsPresenceReconciliation ? session.LastSeenAt : capturedAt;
                durations.Add(Math.Max(0, (long)(endedAt - session.StartedAt).TotalSeconds));
                _sessions.Remove(missing);
                leaves++;
            }

            foreach (var accumulator in EnsureBuckets(capturedAt))
                accumulator.RecordPlayerTransitions(joins, leaves, durations);
            _needsPresenceReconciliation = false;
            _lastPresenceAt = capturedAt;
            await FlushAndPersistAsync(capturedAt, cancellationToken);
        }, "player presence", cancellationToken);

    public Task RecordEventAsync(PalOpsEvent palOpsEvent, CancellationToken cancellationToken = default) =>
        ExecuteBestEffortAsync(async () =>
        {
            if (!IsTrackedEvent(palOpsEvent.EventType)) return;
            var accumulators = EnsureBuckets(palOpsEvent.OccurredAt);
            if (EndsPlayerSessions(palOpsEvent.EventType))
                CloseActiveSessions(accumulators, palOpsEvent.OccurredAt);
            foreach (var accumulator in accumulators)
                accumulator.RecordEvent(palOpsEvent.EventType);
            await FlushAndPersistAsync(DateTimeOffset.UtcNow, cancellationToken);
        }, "operations event", cancellationToken);

    public async Task<IReadOnlyList<StatisticsMetricPoint>> GetOpenPointsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            return _pending
                .Concat(_open.Values.Select(value => value.ToPoint(now)))
                .GroupBy(static point => point.BucketId, StringComparer.Ordinal)
                .Select(static group => group.OrderBy(point => point.UpdatedAt).Last())
                .OrderBy(static point => point.BucketStart)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteBestEffortAsync(
        Func<Task> action,
        string activity,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Statistics recorder failed while processing {Activity}; collection will retry on the next cycle.", activity);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        var state = await stateStore.LoadAsync(cancellationToken);
        foreach (var point in state.OpenBuckets)
        {
            var accumulator = new StatisticsAccumulator(point.Granularity, point.BucketStart, _timeZone);
            accumulator.Merge(point);
            _open[point.Granularity] = accumulator;
        }
        _pending.AddRange(state.PendingPoints);
        foreach (var session in state.ActiveSessions)
        {
            if (!string.IsNullOrWhiteSpace(session.PlayerKey)) _sessions[session.PlayerKey] = session;
        }
        _hasPresenceBaseline = state.HasPresenceBaseline;
        _needsPresenceReconciliation = state.HasPresenceBaseline;
        _lastPresenceAt = state.LastPresenceAt;
        _lastRetentionAt = state.LastRetentionAt;
        _initialized = true;
    }

    private IReadOnlyList<StatisticsAccumulator> EnsureBuckets(DateTimeOffset occurredAt)
    {
        var result = new List<StatisticsAccumulator>(Granularities.Length);
        foreach (var granularity in Granularities)
        {
            var start = StatisticsBucketClock.Start(occurredAt, granularity, _timeZone);
            if (_open.TryGetValue(granularity, out var current) && current.BucketStart != start)
            {
                if (current.BucketStart > start)
                {
                    // An event can arrive after a newer runtime sample opened the current bucket.
                    // Keep the newer bucket and account for the late event there rather than
                    // discarding live accumulator state or reopening an already persisted bucket.
                    result.Add(current);
                    continue;
                }

                _pending.Add(current.ToPoint(occurredAt));
                _open.Remove(granularity);
            }
            if (!_open.TryGetValue(granularity, out current))
            {
                current = new StatisticsAccumulator(granularity, start, _timeZone);
                _open[granularity] = current;
            }
            result.Add(current);
        }
        return result;
    }

    private async Task FlushAndPersistAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var index = 0; index < _pending.Count;)
        {
            try
            {
                await repository.AppendAsync(_pending[index], cancellationToken);
                _pending.RemoveAt(index);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to flush statistics bucket {BucketId}; it remains queued in state.", _pending[index].BucketId);
                break;
            }
        }

        if (!_lastRetentionAt.HasValue || now - _lastRetentionAt.Value >= TimeSpan.FromMinutes(10))
        {
            try
            {
                await repository.ApplyRetentionAsync(now, cancellationToken);
                _lastRetentionAt = now;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to apply statistics retention policy; cleanup will retry later.");
            }
        }

        var state = new StatisticsRecorderState(
            1,
            _open.Values.Select(value => value.ToPoint(now)).ToArray(),
            _pending.ToArray(),
            _sessions.Values.OrderBy(static session => session.PlayerKey, StringComparer.OrdinalIgnoreCase).ToArray(),
            _hasPresenceBaseline,
            _lastPresenceAt,
            _lastRetentionAt,
            now);
        await stateStore.SaveAsync(state, cancellationToken);
    }

    private void CloseActiveSessions(
        IReadOnlyList<StatisticsAccumulator> accumulators,
        DateTimeOffset endedAt)
    {
        if (_sessions.Count == 0) return;
        var endedSessions = _sessions.Values
            .Where(session => session.StartedAt <= endedAt)
            .ToArray();
        if (endedSessions.Length == 0) return;
        var durations = endedSessions
            .Select(session => Math.Max(0, (long)(endedAt - session.StartedAt).TotalSeconds))
            .ToArray();
        foreach (var accumulator in accumulators)
            accumulator.RecordPlayerTransitions(0, durations.Length, durations);
        foreach (var session in endedSessions) _sessions.Remove(session.PlayerKey);
        _needsPresenceReconciliation = false;
        _lastPresenceAt = endedAt;
    }

    private static bool EndsPlayerSessions(string eventType) => eventType.Trim().ToLowerInvariant() switch
    {
        "server.exited-unexpectedly" or
        "server.started" or
        "server.restarted" or
        "server.stopped" => true,
        _ => false
    };

    private static string PlayerKey(PlayerResponse player)
    {
        var value = !string.IsNullOrWhiteSpace(player.PlayerUid)
            ? player.PlayerUid
            : !string.IsNullOrWhiteSpace(player.UserId)
                ? player.UserId
                : player.Name;
        return value.Trim().ToLowerInvariant();
    }

    private static string DisplayName(PlayerResponse player) =>
        string.IsNullOrWhiteSpace(player.Name) ? PlayerKey(player) : player.Name.Trim();

    private static StatisticsActivePlayerSession Session(string key, PlayerResponse player, DateTimeOffset at) =>
        new(key, DisplayName(player), at, at);

    private static bool IsTrackedEvent(string eventType) => eventType.Trim().ToLowerInvariant() switch
    {
        "server.exited-unexpectedly" or
        "server.stop-timeout" or
        "server.operation.failed" or
        "maintenance.crash-guard.restarting" or
        "maintenance.crash-guard.recovered" or
        "maintenance.crash-guard.failed" or
        "server.started" or
        "server.restarted" or
        "server.stopped" or
        "backup.completed" or
        "backup.failed" or
        "webhook.delivery.succeeded" or
        "webhook.delivery.failed" => true,
        _ => false
    };
}
