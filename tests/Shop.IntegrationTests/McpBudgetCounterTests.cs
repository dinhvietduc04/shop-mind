using Shop.Application.Mcp;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.RateLimiting;

namespace Shop.IntegrationTests;

/// <summary>
/// A1 / B3-L2: MCP budgets are sliding-window Redis counters, independent of
/// the REST buckets, and fail closed when the store cannot answer.
/// </summary>
public sealed class McpBudgetCounterTests : IAsyncLifetime
{
    private readonly TestRedis _redis = new();

    public Task InitializeAsync() => _redis.InitializeAsync();
    public Task DisposeAsync() => _redis.DisposeAsync();

    private RedisMcpBudgetCounter NewCounter(int readPerMin = 5, int windowSeconds = 60) =>
        new(
            new McpOptions
            {
                ReadPerMin = readPerMin,
                WritePerMin = 3,
                QuotePerMin = 2,
                WindowSeconds = windowSeconds,
            },
            _redis.ConnectionString,
            _redis.KeyPrefix + "mcp:");

    [Fact]
    public async Task First_read_is_allowed_and_reports_remaining()
    {
        var counter = NewCounter(readPerMin: 5);

        var decision = await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read);

        Assert.True(decision.Allowed);
        Assert.Equal(5, decision.Limit);
        Assert.Equal(4, decision.Remaining);
    }

    [Fact]
    public async Task Burst_beyond_limit_is_denied_with_a_retry_after()
    {
        var counter = NewCounter(readPerMin: 3);

        for (var i = 0; i < 3; i++)
            Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);

        var denied = await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read);

        Assert.False(denied.Allowed);
        Assert.Equal(0, denied.Remaining);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);
        Assert.True(denied.RetryAfter <= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Different_partitions_do_not_share_a_budget()
    {
        var counter = NewCounter(readPerMin: 1);

        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
        Assert.True((await counter.TryConsumeAsync("user:bob", ToolRiskClass.Read)).Allowed);
        Assert.False((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
    }

    [Fact]
    public async Task Risk_classes_have_independent_budgets()
    {
        var counter = NewCounter(readPerMin: 1);

        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Write)).Allowed);
        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Quote)).Allowed);
        Assert.False((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
    }

    [Fact]
    public async Task Window_expiry_releases_the_budget()
    {
        var counter = NewCounter(readPerMin: 1, windowSeconds: 1);

        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
        Assert.False((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);

        await Task.Delay(1200);

        Assert.True((await counter.TryConsumeAsync("user:alice", ToolRiskClass.Read)).Allowed);
    }

    [Fact]
    public async Task Unreachable_redis_fails_closed()
    {
        using var dead = new RedisMcpBudgetCounter(
            new McpOptions(),
            "127.0.0.1:1",
            "shopmindtest:dead:mcp:",
            connectTimeoutMs: 200);

        await Assert.ThrowsAsync<RateLimitUnavailableException>(() =>
            dead.TryConsumeAsync("user:alice", ToolRiskClass.Read));
    }
}
