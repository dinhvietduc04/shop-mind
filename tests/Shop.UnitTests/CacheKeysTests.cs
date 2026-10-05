using Shop.Application.Caching;
using Shop.Application.Dtos;

namespace Shop.UnitTests;

public class CacheKeysTests
{
    [Fact]
    public void ProductKey_Format()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal($"shop:product:{id:D}", CacheKeys.Product(id));
    }

    [Fact]
    public void CategoriesAll_Key()
    {
        Assert.Equal("shop:categories:all", CacheKeys.CategoriesAll);
    }

    [Fact]
    public void ProductsQuery_Deterministic_And_Prefixed()
    {
        var q = new ProductQueryParams("laptop", "laptops", "Dell", 100, 1200, true, "price_asc", 1, 20);
        var a = CacheKeys.ProductsQuery(q);
        var b = CacheKeys.ProductsQuery(q);
        Assert.Equal(a, b);
        Assert.StartsWith(CacheKeys.ProductsQueryPrefix, a);
        Assert.StartsWith("shop:products:q:", a);
    }

    [Fact]
    public void ProductsQuery_Normalizes_Case_And_Whitespace()
    {
        var a = CacheKeys.ProductsQuery(new ProductQueryParams("  Laptop ", "LAPTOPS", " Dell ", null, null, null, "Price_ASC", 1, 20));
        var b = CacheKeys.ProductsQuery(new ProductQueryParams("laptop", "laptops", "dell", null, null, null, "price_asc", 1, 20));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ProductsQuery_Distinguishes_Filters()
    {
        var baseline = CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 1, 20));
        Assert.NotEqual(baseline, CacheKeys.ProductsQuery(new ProductQueryParams("x", null, null, null, null, null, null, 1, 20)));
        Assert.NotEqual(baseline, CacheKeys.ProductsQuery(new ProductQueryParams(null, "cat", null, null, null, null, null, 1, 20)));
        Assert.NotEqual(baseline, CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, 10, null, null, null, 1, 20)));
        Assert.NotEqual(baseline, CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 2, 20)));
    }

    [Fact]
    public void ProductsQuery_Caps_PageSize_Like_Service()
    {
        // ProductService caps pageSize<=0|>100 → 20, so keys must match after capping.
        var over = CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 1, 500));
        var capped = CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 1, 20));
        Assert.Equal(capped, over);

        var zero = CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 0, 0));
        var one = CacheKeys.ProductsQuery(new ProductQueryParams(null, null, null, null, null, null, null, 1, 20));
        Assert.Equal(one, zero);
    }

    [Fact]
    public void Ttls_Match_Plan()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), CacheDurations.Categories);
        Assert.Equal(TimeSpan.FromMinutes(5), CacheDurations.Product);
        // Spec: query TTL 1–2 min.
        Assert.InRange(CacheDurations.ProductsQuery, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void RedisOptions_Defaults()
    {
        var o = new RedisOptions();
        Assert.True(o.Enabled);
        Assert.Equal(90, o.DefaultTtlSeconds);
        Assert.False(string.IsNullOrWhiteSpace(o.ConnectionString));
    }
}
