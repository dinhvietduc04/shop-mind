namespace Shop.Application.Caching;

/// <summary>
/// M9: cache abstraction. Implementations must never throw to callers —
/// cache failure falls through to the database (log + MISS).
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Stampede-guarded read-through. Returns the value and whether it came from cache.
    /// Factory exceptions propagate and are never cached.
    /// </summary>
    Task<(T Value, bool FromCache)> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default);
}
