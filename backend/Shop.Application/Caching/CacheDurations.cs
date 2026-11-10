namespace Shop.Application.Caching;

/// <summary>M9 TTL policy: categories 10m, product 5m, product-list queries 90s (within 1–2m spec).</summary>
public static class CacheDurations
{
    public static readonly TimeSpan Categories = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Product = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ProductsQuery = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan Facets = TimeSpan.FromMinutes(10);
}
