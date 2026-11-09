using Shop.Domain.Exceptions;
using StackExchange.Redis;

namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// B3-L1: fixed-window permit counter in Redis — INCR + first-hit PEXPIRE in one
/// Lua script (atomic, race-free across instances), PTTL for Retry-After math.
/// Fail-closed: a store that cannot answer throws <see cref="RateLimitUnavailableException"/>.
/// </summary>
public sealed class RedisRateLimitCounter : IDisposable
{
    // KEYS[1]=counter key, ARGV[1]=window ms
    // -> { count, ttlMs } (ttl is never -1: the first increment sets it atomically)
    private const string FixedWindowScript = """
        local c = redis.call('INCR', KEYS[1])
        if c == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return { c, redis.call('PTTL', KEYS[1]) }
        """;

    private readonly RateLimitOptions _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private IConnectionMultiplexer? _mux;

    public RedisRateLimitCounter(RateLimitOptions options) => _options = options;

    public async Task<RateLimitDecision> ReserveAsync(
        string bucket, string partition, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        var key = $"{_options.KeyPrefix}{bucket}:{partition}";
        try
        {
            var result = await db.ScriptEvaluateAsync(
                FixedWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { (long)window.TotalMilliseconds });

            var values = (RedisValue[])result!;
            var count = (long)values[0];
            var ttlMs = (long)values[1];
            if (ttlMs < 0) ttlMs = (long)window.TotalMilliseconds; // defensive: key somehow lost its TTL

            var allowed = count <= limit;
            return new RateLimitDecision(
                Allowed: allowed,
                Limit: limit,
                Remaining: allowed ? (int)(limit - count) : 0,
                WindowRemaining: TimeSpan.FromMilliseconds(Math.Max(0, ttlMs)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RateLimitUnavailableException($"{bucket}: {ex.Message}");
        }
    }

    /// <summary>Lazy connect, same self-contained pattern as the cache and B2 store.</summary>
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
                        $"{_options.ConnectionString},connectTimeout={_options.ConnectTimeoutMs},connectRetry=1,abortConnect=true");
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
