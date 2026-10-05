using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shop.Application.Dtos;

namespace Shop.Application.Caching;

/// <summary>
/// M9: canonical cache keys + TTLs. Contracts (locked):
/// <c>shop:product:{id}</c>, <c>shop:products:q:{sha1}</c>, <c>shop:categories:all</c>.
/// Only public catalog reads are cached — never cart/checkout/orders/auth.
/// </summary>
public static class CacheKeys
{
    public const string CategoriesAll = "shop:categories:all";
    public const string ProductsQueryPrefix = "shop:products:q:";

    public static string Product(Guid id) => $"shop:product:{id:D}";

    public static string ProductsQuery(ProductQueryParams query)
    {
        // Normalize exactly like ProductService caps them so equivalent
        // requests share one key: page<=0→1, pageSize<=0|>100→20.
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize is <= 0 or > 100 ? 20 : query.PageSize;
        var raw = string.Join("|",
            (query.Search ?? string.Empty).Trim().ToLowerInvariant(),
            (query.Category ?? string.Empty).Trim().ToLowerInvariant(),
            (query.Brand ?? string.Empty).Trim().ToLowerInvariant(),
            query.MinPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            query.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            (query.InStockOnly ?? false).ToString(),
            (query.Sort ?? string.Empty).Trim().ToLowerInvariant(),
            page.ToString(CultureInfo.InvariantCulture),
            pageSize.ToString(CultureInfo.InvariantCulture),
            query.IncludeInactive.ToString());
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return ProductsQueryPrefix + hash;
    }
}

/// <summary>M9 TTL policy: categories 10m, product 5m, product-list queries 90s (within 1–2m spec).</summary>
public static class CacheDurations
{
    public static readonly TimeSpan Categories = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Product = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ProductsQuery = TimeSpan.FromSeconds(90);
}
