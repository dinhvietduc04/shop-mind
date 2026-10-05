using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shop.Application.Caching;
using Shop.Infrastructure.Caching;

namespace Shop.UnitTests;

public class RedisCacheServiceTests
{
    private sealed class StaticOptions(RedisOptions value) : IOptionsMonitor<RedisOptions>
    {
        public RedisOptions CurrentValue => value;
        public RedisOptions Get(string? name) => value;
        public IDisposable OnChange(Action<RedisOptions, string?> listener) => NullDisposable.Instance;
        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task DisabledCache_FallsThrough_ToFactory_And_NeverThrows()
    {
        var svc = new RedisCacheService(
            new StaticOptions(new RedisOptions { Enabled = false }),
            NullLogger<RedisCacheService>.Instance);

        Assert.Null(await svc.GetAsync<string>("shop:product:00000000-0000-0000-0000-000000000000"));

        // Set/Remove/evict are best-effort no-ops when disabled.
        await svc.SetAsync("k", "v", TimeSpan.FromMinutes(1));
        await svc.RemoveAsync("k");
        await svc.RemoveByPrefixAsync("shop:products:q:");

        var (value, fromCache) = await svc.GetOrCreateAsync("k", () => Task.FromResult("live"));
        Assert.Equal("live", value);
        Assert.False(fromCache);

        svc.Dispose();
    }
}
