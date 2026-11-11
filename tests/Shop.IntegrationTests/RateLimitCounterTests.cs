using Microsoft.Extensions.Options;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.RateLimiting;
using StackExchange.Redis;

namespace Shop.IntegrationTests;

/// <summary>
/// B3-L1: fixed-window rate permits in Redis (check+INCR+PEXPIRE atomicity, PTTL
/// drives Retry-After, refusals never consume a permit). Fail-closed per
/// ADR-0004: an unreachable store denies.
/// Seam: RedisRateLimitCounter against the shared test Redis.
/// </summary>
public sealed class RateLimitCounterTests : IAsyncLifetime
{
    private readonly TestRedis _redis = new();

    public Task InitializeAsync() => _redis.InitializeAsync();
    public Task DisposeAsync() => _redis.DisposeAsync();

    private string KeyPrefix => _redis.KeyPrefix + "rl:";

    private RedisRateLimitCounter NewCounter() => new(new StaticOptions(new RateLimitOptions
    {
        ConnectionString = _redis.ConnectionString,
        KeyPrefix = KeyPrefix,
        WindowSeconds = 60,
    }));

    private sealed class StaticOptions(RateLimitOptions value) : IOptionsMonitor<RateLimitOptions>
    {
        public RateLimitOptions CurrentValue => value;
        public RateLimitOptions Get(string? name) => value;
        public IDisposable OnChange(Action<RateLimitOptions, string?> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task FirstRequestInFreshWindow_IsAllowedAndReportsRemaining()
    {
        var counter = NewCounter();

        var d = await counter.ReserveAsync("catalog", "ip:1.2.3.4", limit: 5, Minute);

        Assert.True(d.Allowed);
        Assert.Equal(5, d.Limit);
        Assert.Equal(4, d.Remaining);
        Assert.InRange(d.WindowRemaining, TimeSpan.Zero, Minute);
    }

    [Fact]
    public async Task BurstBeyondLimit_RejectsAndReportsWindowRemainingForRetryAfter()
    {
        var counter = NewCounter();

        for (var i = 0; i < 3; i++)
        {
            var d = await counter.ReserveAsync("auth", "ip:9.9.9.9", limit: 3, Minute);
            Assert.True(d.Allowed);
            Assert.Equal(2 - i, d.Remaining);
        }

        var blocked = await counter.ReserveAsync("auth", "ip:9.9.9.9", limit: 3, Minute);
        Assert.False(blocked.Allowed);
        Assert.Equal(0, blocked.Remaining);
        Assert.True(blocked.WindowRemaining > TimeSpan.Zero);
        Assert.True(blocked.WindowRemaining <= Minute);
    }

    [Fact]
    public async Task DifferentPartitions_DoNotShareBudget()
    {
        var counter = NewCounter();

        var a = await counter.ReserveAsync("cart", "u:alice", limit: 1, Minute);
        var b = await counter.ReserveAsync("cart", "u:bob", limit: 1, Minute);
        var a2 = await counter.ReserveAsync("cart", "u:alice", limit: 1, Minute);

        Assert.True(a.Allowed);
        Assert.True(b.Allowed);
        Assert.False(a2.Allowed);
    }

    [Fact]
    public async Task WindowExpires_ShortWindowResetsAndAllowsAgain()
    {
        var counter = NewCounter();
        var window = TimeSpan.FromSeconds(1);

        Assert.True((await counter.ReserveAsync("global", "ip:x", limit: 1, window)).Allowed);
        Assert.False((await counter.ReserveAsync("global", "ip:x", limit: 1, window)).Allowed);

        await Task.Delay(1400); // window lapses
        Assert.True((await counter.ReserveAsync("global", "ip:x", limit: 1, window)).Allowed);
    }

    [Fact]
    public async Task RefusedRequests_DoNotConsumePermits()
    {
        var counter = NewCounter();

        Assert.True((await counter.ReserveAsync("global", "ip:z", limit: 2, Minute)).Allowed);
        Assert.True((await counter.ReserveAsync("global", "ip:z", limit: 2, Minute)).Allowed);

        for (var i = 0; i < 5; i++)
            Assert.False((await counter.ReserveAsync("global", "ip:z", limit: 2, Minute)).Allowed);

        // The stored count is "admitted this window", so a flood of refusals
        // cannot grow it (or the key) without bound.
        using var mux = await ConnectionMultiplexer.ConnectAsync(
            $"{_redis.ConnectionString},connectTimeout=2000,abortConnect=false");
        var stored = await mux.GetDatabase().StringGetAsync($"{KeyPrefix}global:ip:z");
        Assert.Equal("2", stored.ToString());
    }

    [Fact]
    public async Task UnreachableRedis_FailsClosedThrows()
    {
        using var dead = new RedisRateLimitCounter(new StaticOptions(new RateLimitOptions
        {
            ConnectionString = "127.0.0.1:1",
            KeyPrefix = "shopmindtest:dead:rl:",
            ConnectTimeoutMs = 200,
        }));

        var act = async () => await dead.ReserveAsync("global", "ip:x", limit: 5, Minute);
        await Assert.ThrowsAsync<RateLimitUnavailableException>(act);
    }
}
