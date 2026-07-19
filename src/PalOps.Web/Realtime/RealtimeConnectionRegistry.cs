using System.Collections.Concurrent;

namespace PalOps.Web.Realtime;

public sealed record RealtimeConnectionPreference(
    string ConnectionId,
    string UserName,
    string Role,
    string Mode,
    int? RefreshSeconds,
    bool PageVisible,
    DateTimeOffset NextDueAt);

public interface IRealtimeConnectionRegistry
{
    void Add(string connectionId, string userName, string role);
    void Remove(string connectionId);
    RealtimeConnectionPreference SetMode(string connectionId, string mode);
    RealtimeConnectionPreference SetVisibility(string connectionId, bool visible);
    IReadOnlyList<RealtimeConnectionPreference> GetDue(DateTimeOffset now);
    void MarkSent(string connectionId, DateTimeOffset now);
}

public sealed class RealtimeConnectionRegistry : IRealtimeConnectionRegistry
{
    public const int MinimumRefreshSeconds = 1;
    public const int MaximumRefreshSeconds = 30;
    public const int DefaultRefreshSeconds = 10;
    private const int BackgroundMinimumRefreshSeconds = 15;

    private readonly ConcurrentDictionary<string, RealtimeConnectionPreference> _connections = new(StringComparer.Ordinal);

    public void Add(string connectionId, string userName, string role)
    {
        var now = DateTimeOffset.UtcNow;
        _connections[connectionId] = new(
            connectionId,
            userName,
            role,
            $"{DefaultRefreshSeconds}s",
            DefaultRefreshSeconds,
            true,
            now.AddSeconds(DefaultRefreshSeconds));
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public RealtimeConnectionPreference SetMode(string connectionId, string mode)
    {
        var parsed = Parse(mode);
        return Update(connectionId, current =>
        {
            var changed = current with { Mode = parsed.Mode, RefreshSeconds = parsed.Seconds };
            return changed with { NextDueAt = NextDue(changed, DateTimeOffset.UtcNow) };
        });
    }

    public RealtimeConnectionPreference SetVisibility(string connectionId, bool visible) =>
        Update(connectionId, current =>
        {
            var changed = current with { PageVisible = visible };
            return changed with { NextDueAt = NextDue(changed, DateTimeOffset.UtcNow) };
        });

    public IReadOnlyList<RealtimeConnectionPreference> GetDue(DateTimeOffset now) =>
        _connections.Values
            .Where(preference => preference.RefreshSeconds.HasValue && preference.NextDueAt <= now)
            .OrderBy(preference => preference.NextDueAt)
            .ToArray();

    public void MarkSent(string connectionId, DateTimeOffset now)
    {
        _connections.AddOrUpdate(connectionId,
            _ => throw new KeyNotFoundException("实时连接不存在。"),
            (_, current) => current with { NextDueAt = NextDue(current, now) });
    }

    private RealtimeConnectionPreference Update(
        string connectionId,
        Func<RealtimeConnectionPreference, RealtimeConnectionPreference> update)
    {
        while (true)
        {
            if (!_connections.TryGetValue(connectionId, out var current))
                throw new KeyNotFoundException("实时连接不存在。");
            var next = update(current);
            if (_connections.TryUpdate(connectionId, next, current)) return next;
        }
    }

    private static (string Mode, int? Seconds) Parse(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        if (normalized == "manual") return ("manual", null);
        if (normalized is null || !normalized.EndsWith('s')
            || !int.TryParse(normalized[..^1], out var seconds)
            || seconds is < MinimumRefreshSeconds or > MaximumRefreshSeconds)
            throw new ArgumentException("刷新模式只能为 1s-30s 或 manual。", nameof(mode));
        return ($"{seconds}s", seconds);
    }

    private static DateTimeOffset NextDue(RealtimeConnectionPreference preference, DateTimeOffset now)
    {
        if (!preference.RefreshSeconds.HasValue) return DateTimeOffset.MaxValue;
        var seconds = preference.RefreshSeconds.Value;
        if (!preference.PageVisible) seconds = Math.Max(seconds, BackgroundMinimumRefreshSeconds);
        return now.AddSeconds(seconds);
    }
}
