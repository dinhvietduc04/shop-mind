using Shop.Application.Mcp;
using Shop.Domain.Exceptions;
using StackExchange.Redis;

namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// A1 / B3-L2: sliding-window MCP budgets in Redis. The sorted set keeps one
/// member per accepted call; expired members are trimmed before each check.
/// Fail-closed: a store that cannot answer throws
/// <see cref="RateLimitUnavailableException"/> instead of running unthrottled.
/// </summary>
public sealed class RedisMcpBudgetCounter : IMcpBudgetCounter, IDisposable
{
    // KEYS[1] = budget key
    // ARGV[1] = now (unix ms), [2] = window ms, [3] = limit, [4] = unique member
    // -> { allowed (1/0), remaining, retryAfterMs }
    private const string SlidingWindowScript = """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]

        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local count = redis.call('ZCARD', key)
        if count < limit then
            redis.call('ZADD', key, now, member)
            redis.call('PEXPIRE', key, window)
            return { 1, limit - count - 1, window }
        end

        local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
        local retry = window
        if oldest and oldest[2] then
            retry = math.max(0, tonumber(oldest[2]) + window - now)
        end
        return { 0, 0, retry }
        """;

    private readonly McpOptions _options;
    private readonly string _connectionString;
    private readonly string _keyPrefix;
    private readonly int _connectTimeoutMs;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private IConnectionMultiplexer? _mux;

    public RedisMcpBudgetCounter(
        McpOptions options,
        string connectionString,
        string keyPrefix = "shop:mcp:rl:",
        int connectTimeoutMs = 1000)
    {
        _options = options;
        _connectionString = connectionString;
        _keyPrefix = keyPrefix;
        _connectTimeoutMs = connectTimeoutMs;
    }

    public async Task<McpBudgetDecision> TryConsumeAsync(
        string partition,
        ToolRiskClass riskClass,
        CancellationToken ct = default)
    {
        var limit = riskClass switch
        {
            ToolRiskClass.Read => _options.ReadPerMin,
            ToolRiskClass.Write => _options.WritePerMin,
            ToolRiskClass.Quote => _options.QuotePerMin,
            _ => 0,
        };

        var window = TimeSpan.FromSeconds(_options.WindowSeconds);
        if (limit <= 0)
            return new McpBudgetDecision(false, 0, 0, window);

        var db = await DatabaseAsync(ct);
        var key = $"{_keyPrefix}{riskClass.ToString().ToLowerInvariant()}:{partition}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)window.TotalMilliseconds;
        var member = $"{now}:{Guid.NewGuid():N}";

        try
        {
            var result = await db.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { now, windowMs, limit, member });

            var values = (RedisValue[])result!;
            var allowed = (long)values[0] == 1;
            var remaining = (int)(long)values[1];
            var retryMs = Math.Max(0, (long)values[2]);

            return new McpBudgetDecision(
                allowed,
                limit,
                remaining,
                TimeSpan.FromMilliseconds(retryMs));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RateLimitUnavailableException($"mcp:{riskClass}: {ex.Message}");
        }
    }

    private async Task<IDatabase> DatabaseAsync(CancellationToken ct)
    {
        var mux = _mux;
        if (mux is null || !mux.IsConnected)
        {
            await _connectGate.WaitAsync(ct);
            try
            {
                if (_mux is null || !_mux.IsConnected)
                {
                    var config = ConfigurationOptions.Parse(
                        $"{_connectionString},connectTimeout={_connectTimeoutMs},connectRetry=1,abortConnect=true");
                    try
                    {
                        var fresh = await ConnectionMultiplexer.ConnectAsync(config);
                        var old = Interlocked.Exchange(ref _mux, fresh);
                        old?.Dispose();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        throw new RateLimitUnavailableException(ex.Message);
                    }
                }
            }
            finally
            {
                _connectGate.Release();
            }
        }

        return _mux!.GetDatabase();
    }

    public void Dispose()
    {
        _mux?.Dispose();
        _connectGate.Dispose();
    }
}
