namespace PalOps.Web.Platform.Caching;

public sealed record PlatformCacheNamespaceSnapshot(
    string Name,
    int Entries,
    long Hits,
    long Misses);

public sealed record PlatformCacheSnapshot(
    long Hits,
    long Misses,
    long FactoryExecutions,
    long Evictions,
    int EntryCount,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PlatformCacheNamespaceSnapshot> Namespaces)
{
    public double HitRate => Hits + Misses == 0 ? 0 : Math.Round(Hits * 100d / (Hits + Misses), 2);
}

public interface IPlatformCache
{
    bool TryGet<T>(string key, out T? value);
    void Set<T>(string key, T value, TimeSpan lifetime, IReadOnlyCollection<string>? tags = null);
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);
    void Remove(string key);
    int RemoveByTag(string tag);
    PlatformCacheSnapshot GetSnapshot();
}
