using System.Collections.Concurrent;

namespace PalOps.Web.Security;

public sealed record LoginAttemptState(bool IsLocked, int FailureCount, TimeSpan RetryAfter);

public interface ILoginAttemptTracker
{
    LoginAttemptState GetState(string key);
    void RecordFailure(string key);
    void RecordSuccess(string key);
}

public sealed class LoginAttemptTracker : ILoginAttemptTracker
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxFailures;
    private readonly TimeSpan _lockDuration;

    public LoginAttemptTracker()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public LoginAttemptTracker(Func<DateTimeOffset> clock, int maxFailures = 5, TimeSpan? lockDuration = null)
    {
        _clock = clock;
        _maxFailures = maxFailures;
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(15);
    }

    public LoginAttemptState GetState(string key)
    {
        var normalized = NormalizeKey(key);
        if (!_entries.TryGetValue(normalized, out var entry))
        {
            return new LoginAttemptState(false, 0, TimeSpan.Zero);
        }

        lock (entry.Gate)
        {
            var now = _clock();
            if (entry.LockedUntil is { } lockedUntil && lockedUntil > now)
            {
                return new LoginAttemptState(true, entry.FailureCount, lockedUntil - now);
            }

            if (entry.LockedUntil is not null)
            {
                entry.FailureCount = 0;
                entry.LockedUntil = null;
            }

            return new LoginAttemptState(false, entry.FailureCount, TimeSpan.Zero);
        }
    }

    public void RecordFailure(string key)
    {
        var entry = _entries.GetOrAdd(NormalizeKey(key), static _ => new Entry());
        lock (entry.Gate)
        {
            var now = _clock();
            if (entry.LockedUntil is { } lockedUntil && lockedUntil > now)
            {
                return;
            }

            if (entry.LockedUntil is not null)
            {
                entry.FailureCount = 0;
                entry.LockedUntil = null;
            }

            entry.FailureCount++;
            if (entry.FailureCount >= _maxFailures)
            {
                entry.LockedUntil = now.Add(_lockDuration);
            }
        }
    }

    public void RecordSuccess(string key)
    {
        _entries.TryRemove(NormalizeKey(key), out _);
    }

    private static string NormalizeKey(string key) => string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public int FailureCount { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
