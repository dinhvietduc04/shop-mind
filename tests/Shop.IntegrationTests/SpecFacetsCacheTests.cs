using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Application.Caching;

namespace Shop.IntegrationTests;

/// <summary>A3: facets are category-scoped and cached for 10 minutes (ADR-0007).</summary>
public class SpecFacetsCacheTests : IntegrationTestBase<ShopWebFactory>
{
    private readonly ShopWebFactory factory;

    public SpecFacetsCacheTests(ShopWebFactory factory) : base(factory) => this.factory = factory;

    private sealed class RecordingCache : ICacheService
    {
        private readonly ConcurrentDictionary<string, object> _store = new();
        public ConcurrentDictionary<string, TimeSpan?> Ttls { get; } = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out var value) && value is T typed ? typed : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            if (value is not null)
            {
                _store[key] = value;
                Ttls[key] = ttl;
            }
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            Ttls.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _store.TryRemove(key, out _);
                Ttls.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public async Task<(T Value, bool FromCache)> GetOrCreateAsync<T>(
            string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var cached) && cached is T typed) return (typed, true);
            var value = await factory();
            await SetAsync(key, value, ttl, ct);
            return (value, false);
        }
    }

    [Fact]
    public async Task Facets_are_cached_for_ten_minutes_per_category()
    {
        var cache = new RecordingCache();
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ICacheService>();
            s.AddSingleton<ICacheService>(cache);
        })).CreateClient();

        var first = await client.GetAsync("/api/products/facets?category=laptops");
        Assert.Equal("MISS", CacheHeader(first));
        var second = await client.GetAsync("/api/products/facets?category=laptops");
        Assert.Equal("HIT", CacheHeader(second));

        Assert.Equal(TimeSpan.FromMinutes(10), cache.Ttls["shop:facets:laptops"]);

        var otherCategory = await client.GetAsync("/api/products/facets?category=headphones");
        Assert.Equal("MISS", CacheHeader(otherCategory));
        Assert.True(cache.Ttls.ContainsKey("shop:facets:headphones"));
    }

    [Fact]
    public async Task Catalog_writes_evict_the_facets_cache()
    {
        var cache = new RecordingCache();
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ICacheService>();
            s.AddSingleton<ICacheService>(cache);
        })).CreateClient();

        Assert.Equal("MISS", CacheHeader(await client.GetAsync("/api/products/facets?category=laptops")));
        Assert.Equal("HIT", CacheHeader(await client.GetAsync("/api/products/facets?category=laptops")));

        var products = await client.GetFromJsonAsync<PagedResult>("/api/products?category=laptops&pageSize=1");
        var product = products!.Items[0];
        var admin = await RegisterAndLoginAsync("admin@shopmind.local", "Admin123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);

        var current = await client.GetFromJsonAsync<ProductDetail>($"/api/admin/products/{product.Id}");
        var update = await client.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
        {
            name = current!.Name,
            slug = current.Slug,
            sku = current.Sku,
            categoryId = current.CategoryId,
            description = current.Description,
            brand = current.Brand,
            price = current.Price + 1m,
            status = 1,
            images = (object?)null,
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());

        Assert.Equal("MISS", CacheHeader(await client.GetAsync("/api/products/facets?category=laptops")));

        // Warm again, then prove an inventory write also invalidates facets (B6).
        Assert.Equal("HIT", CacheHeader(await client.GetAsync("/api/products/facets?category=laptops")));
        var inv = await client.GetFromJsonAsync<InventoryDoc>($"/api/admin/inventory/{product.Id}");
        var patch = await client.PatchAsJsonAsync($"/api/admin/inventory/{product.Id}", new { quantity = inv!.Quantity + 1 });
        Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync());

        Assert.Equal("MISS", CacheHeader(await client.GetAsync("/api/products/facets?category=laptops")));
    }

    private static string? CacheHeader(HttpResponseMessage res) =>
        res.Headers.TryGetValues("X-Cache", out var v) ? v.FirstOrDefault() : null;

    private record PagedResult(List<Item> Items);
    private record Item(Guid Id);
    private record ProductDetail(Guid Id, string Name, string Slug, string Sku, Guid CategoryId,
        string? Description, string? Brand, decimal Price);
    private record InventoryDoc(int Quantity);
}
