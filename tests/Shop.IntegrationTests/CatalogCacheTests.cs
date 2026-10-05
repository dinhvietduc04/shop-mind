using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Application.Caching;

namespace Shop.IntegrationTests;

/// <summary>M9: catalog caching — HIT/MISS headers, write→evict, admin bypass, Redis-down fallback.</summary>
public class CatalogCacheTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    // Deterministic in-memory ICacheService: honors TTL, prefix eviction,
    // and GetOrCreate single-flight enough for controller-level tests.
    private sealed class TestCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, (object Value, DateTime Expires)> _store = new();
        public bool ThrowAll { get; set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (ThrowAll) throw new InvalidOperationException("redis down (simulated)");
            if (_store.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
                return Task.FromResult((T?)entry.Value);
            _store.TryRemove(key, out _);
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            if (ThrowAll) throw new InvalidOperationException("redis down (simulated)");
            if (value is null) return Task.CompletedTask;
            _store[key] = (value!, DateTime.UtcNow + (ttl ?? TimeSpan.FromSeconds(90)));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            if (ThrowAll) throw new InvalidOperationException("redis down (simulated)");
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            if (ThrowAll) throw new InvalidOperationException("redis down (simulated)");
            foreach (var k in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _store.TryRemove(k, out _);
            return Task.CompletedTask;
        }

        public async Task<(T Value, bool FromCache)> GetOrCreateAsync<T>(
            string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            // Mirror RedisCacheService resilience: cache failure → run factory, report MISS.
            T? cached = default;
            var hit = false;
            try
            {
                cached = await GetAsync<T>(key, ct);
                hit = cached is not null;
            }
            catch
            {
                hit = false;
            }
            if (hit) return (cached!, true);
            var value = await factory();
            try
            {
                await SetAsync(key, value, ttl, ct);
            }
            catch
            {
                // best-effort
            }
            return (value, false);
        }
    }

    private HttpClient ClientWithCache(TestCacheService cache)
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ICacheService>();
            s.AddSingleton<ICacheService>(cache);
        }));
        return f.CreateClient();
    }

    private static string? CacheHeader(HttpResponseMessage res)
        => res.Headers.TryGetValues("X-Cache", out var v) ? v.FirstOrDefault() : null;

    private async Task<Guid> FirstProductIdAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<PagedResult>("/api/products?page=1&pageSize=5");
        Assert.NotNull(products);
        Assert.NotEmpty(products!.Items);
        return products.Items[0].Id;
    }

    private async Task<string> AdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@shopmind.local", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var doc = await login.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(doc);
        return doc!.Token;
    }

    [Fact]
    public async Task ProductById_MissThenHit()
    {
        var client = ClientWithCache(new TestCacheService());
        var id = await FirstProductIdAsync(client);

        var first = await client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("MISS", CacheHeader(first));

        var second = await client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("HIT", CacheHeader(second));

        var a = await first.Content.ReadAsStringAsync();
        var b = await second.Content.ReadAsStringAsync();
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task ProductsQuery_MissThenHit()
    {
        var client = ClientWithCache(new TestCacheService());

        var first = await client.GetAsync("/api/products?page=1&pageSize=5");
        Assert.Equal("MISS", CacheHeader(first));

        var second = await client.GetAsync("/api/products?page=1&pageSize=5");
        Assert.Equal("HIT", CacheHeader(second));
    }

    [Fact]
    public async Task Categories_MissThenHit()
    {
        var client = ClientWithCache(new TestCacheService());

        var first = await client.GetAsync("/api/categories");
        Assert.Equal("MISS", CacheHeader(first));

        var second = await client.GetAsync("/api/categories");
        Assert.Equal("HIT", CacheHeader(second));
    }

    [Fact]
    public async Task ProductUpdate_EvictsCache_ReflectsNewPrice()
    {
        var cache = new TestCacheService();
        var client = ClientWithCache(cache);
        var id = await FirstProductIdAsync(client);

        var warm = await client.GetAsync($"/api/products/{id}");
        Assert.Equal("MISS", CacheHeader(warm));
        var hit = await client.GetAsync($"/api/products/{id}");
        Assert.Equal("HIT", CacheHeader(hit));

        // Admin update (goes through ProductService → evicts product:{id} + queries).
        var admin = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var current = await client.GetFromJsonAsync<ProductDetail>($"/api/admin/products/{id}");
        Assert.NotNull(current);
        var newPrice = current!.Price + 1.5m;
        var update = await client.PutAsJsonAsync($"/api/admin/products/{id}", new
        {
            name = current.Name,
            slug = current.Slug,
            sku = current.Sku,
            categoryId = current.CategoryId,
            description = current.Description,
            brand = current.Brand,
            price = newPrice,
            status = 1, // Active
            images = (object?)null
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = null;

        var after = await client.GetAsync($"/api/products/{id}");
        Assert.Equal("MISS", CacheHeader(after));
        var doc = await after.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.Equal(newPrice, doc!.Price);
    }

    [Fact]
    public async Task CategoryWrite_EvictsCategoriesList()
    {
        var client = ClientWithCache(new TestCacheService());

        var warm = await client.GetAsync("/api/categories");
        Assert.Equal("MISS", CacheHeader(warm));
        Assert.Equal("HIT", CacheHeader(await client.GetAsync("/api/categories")));

        var admin = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var created = await client.PostAsJsonAsync("/api/admin/categories", new
        {
            name = $"CacheTest-{Guid.NewGuid():N}",
            slug = (string?)null,
            description = "m9 test",
            isActive = true
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = null;

        var after = await client.GetAsync("/api/categories");
        Assert.Equal("MISS", CacheHeader(after));
    }

    [Fact]
    public async Task InventoryUpdate_EvictsProduct()
    {
        var client = ClientWithCache(new TestCacheService());
        var id = await FirstProductIdAsync(client);

        Assert.Equal("MISS", CacheHeader(await client.GetAsync($"/api/products/{id}")));
        Assert.Equal("HIT", CacheHeader(await client.GetAsync($"/api/products/{id}")));

        var admin = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var inv = await client.GetFromJsonAsync<InventoryDoc>($"/api/admin/inventory/{id}");
        Assert.NotNull(inv);
        var patched = await client.PatchAsJsonAsync($"/api/admin/inventory/{id}", new { quantity = inv!.Quantity + 5 });
        Assert.True(patched.IsSuccessStatusCode, await patched.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal("MISS", CacheHeader(await client.GetAsync($"/api/products/{id}")));
    }

    [Fact]
    public async Task Bypass_AdminOnly()
    {
        var client = ClientWithCache(new TestCacheService());
        var id = await FirstProductIdAsync(client);

        // Anonymous ?nocache=1 is ignored (normal MISS/HIT, never BYPASSED).
        var anon = await client.GetAsync($"/api/products/{id}?nocache=1");
        Assert.NotEqual("BYPASSED", CacheHeader(anon));

        // Prime cache, then admin bypass must not serve stale nor pollute.
        Assert.Equal("HIT", CacheHeader(await client.GetAsync($"/api/products/{id}")));
        var admin = await AdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var bypassed = await client.GetAsync($"/api/products/{id}?nocache=1");
        Assert.Equal("BYPASSED", CacheHeader(bypassed));
        client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal("HIT", CacheHeader(await client.GetAsync($"/api/products/{id}")));
    }

    [Fact]
    public async Task RedisDown_Fallback_Still200()
    {
        var client = ClientWithCache(new TestCacheService { ThrowAll = true });
        var res = await client.GetAsync("/api/products?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = await FirstProductIdAsync(client);
        var single = await client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
    }

    private record PagedResult(List<Item> Items, int Page, int PageSize, int TotalCount);
    private record Item(Guid Id, string Name);
    private record ProductDetail(Guid Id, string Name, string Slug, string Sku, string? Description,
        string? Brand, decimal Price, Guid CategoryId);
    private record InventoryDoc(Guid ProductId, int Quantity);
}
