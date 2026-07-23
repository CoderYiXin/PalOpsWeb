using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace PalOps.Web.Platform.Caching;

/// <summary>
/// Process-local platform cache with stampede protection, tag invalidation and
/// lightweight telemetry. Factories are never cached when they fail or are cancelled.
/// </summary>
public sealed class PlatformMemoryCache(IMemoryCache memoryCache) : IPlatformCache, IDisposable
{
    private sealed record EntryMetadata(Guid Version, string Namespace, IReadOnlyCollection<string> Tags);

    private readonly ConcurrentDictionary<string, EntryMetadata> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _factoryGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _namespaceHits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _namespaceMisses = new(StringComparer.OrdinalIgnoreCase);
    private long _hits;
    private long _misses;
    private long _factoryExecutions;
    private long _evictions;
    private int _disposed;

    public bool TryGet<T>(string key, out T? value)
    {
        ThrowIfDisposed();
        var normalized = NormalizeKey(key);
        var cacheNamespace = NamespaceOf(normalized);
        if (memoryCache.TryGetValue(normalized, out var raw) && raw is T typed)
        {
            Interlocked.Increment(ref _hits);
            _namespaceHits.AddOrUpdate(cacheNamespace, 1, static (_, current) => current + 1);
            value = typed;
            return true;
        }

        Interlocked.Increment(ref _misses);
        _namespaceMisses.AddOrUpdate(cacheNamespace, 1, static (_, current) => current + 1);
        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan lifetime, IReadOnlyCollection<string>? tags = null)
    {
        ThrowIfDisposed();
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        var normalized = NormalizeKey(key);
        var normalizedTags = NormalizeTags(tags);
        var metadata = new EntryMetadata(Guid.NewGuid(), NamespaceOf(normalized), normalizedTags);

        RemoveMetadata(normalized, countEviction: false);
        _entries[normalized] = metadata;
        foreach (var tag in normalizedTags)
            _tagKeys.GetOrAdd(tag, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[normalized] = 0;

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime
        };
        options.RegisterPostEvictionCallback((evictedKey, _, _, state) =>
        {
            var callback = (EvictionCallbackState)state!;
            callback.Owner.OnEvicted((string)evictedKey, callback.Version);
        }, new EvictionCallbackState(this, metadata.Version));
        memoryCache.Set(normalized, value, options);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var normalized = NormalizeKey(key);
        if (TryGet<T>(normalized, out var cached)) return cached!;

        var gate = _factoryGates.GetOrAdd(normalized, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet<T>(normalized, out cached)) return cached!;
            Interlocked.Increment(ref _factoryExecutions);
            var created = await factory(cancellationToken).ConfigureAwait(false);
            Set(normalized, created, lifetime, tags);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remove(string key)
    {
        ThrowIfDisposed();
        var normalized = NormalizeKey(key);
        RemoveMetadata(normalized, countEviction: true);
        memoryCache.Remove(normalized);
    }

    public int RemoveByTag(string tag)
    {
        ThrowIfDisposed();
        var normalizedTag = NormalizeTag(tag);
        if (!_tagKeys.TryRemove(normalizedTag, out var keys)) return 0;
        var removed = 0;
        foreach (var key in keys.Keys)
        {
            if (_entries.ContainsKey(key))
            {
                Remove(key);
                removed++;
            }
        }
        return removed;
    }

    public PlatformCacheSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        var namespaceNames = _entries.Values.Select(static x => x.Namespace)
            .Concat(_namespaceHits.Keys)
            .Concat(_namespaceMisses.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshots = namespaceNames.Select(name => new PlatformCacheNamespaceSnapshot(
            name,
            _entries.Values.Count(item => item.Namespace.Equals(name, StringComparison.OrdinalIgnoreCase)),
            _namespaceHits.TryGetValue(name, out var hits) ? hits : 0,
            _namespaceMisses.TryGetValue(name, out var misses) ? misses : 0)).ToArray();
        return new(
            Interlocked.Read(ref _hits),
            Interlocked.Read(ref _misses),
            Interlocked.Read(ref _factoryExecutions),
            Interlocked.Read(ref _evictions),
            _entries.Count,
            DateTimeOffset.UtcNow,
            snapshots);
    }

    private void OnEvicted(string key, Guid version)
    {
        if (_entries.TryGetValue(key, out var current) && current.Version == version)
            RemoveMetadata(key, countEviction: true);
    }

    private void RemoveMetadata(string key, bool countEviction)
    {
        if (!_entries.TryRemove(key, out var metadata)) return;
        foreach (var tag in metadata.Tags)
        {
            if (!_tagKeys.TryGetValue(tag, out var keys)) continue;
            keys.TryRemove(key, out _);
            if (keys.IsEmpty) _tagKeys.TryRemove(tag, out _);
        }
        if (countEviction) Interlocked.Increment(ref _evictions);
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("缓存键不能为空。", nameof(key));
        return key.Trim();
    }

    private static string NamespaceOf(string key)
    {
        var separator = key.IndexOf(':');
        return separator <= 0 ? "default" : key[..separator].ToLowerInvariant();
    }

    private static IReadOnlyCollection<string> NormalizeTags(IReadOnlyCollection<string>? tags) =>
        tags is null
            ? []
            : tags.Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string NormalizeTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("缓存标签不能为空。", nameof(tag));
        return tag.Trim().ToLowerInvariant();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var gate in _factoryGates.Values) gate.Dispose();
        _factoryGates.Clear();
    }

    private sealed record EvictionCallbackState(PlatformMemoryCache Owner, Guid Version);
}
