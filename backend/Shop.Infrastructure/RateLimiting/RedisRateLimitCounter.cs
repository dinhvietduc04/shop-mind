using Microsoft.Extensions.Options;
using Shop.Domain.Exceptions;
using StackExchange.Redis;

namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// B3-L1: fixed-window permit counter in Redis — check + INCR + first-hit
/// PEXPIRE in one Lua script (atomic, race-free across instances), PTTL for
/// Retry-After math. A refused attempt does NOT consume a permit, so the stored
/// count is "requests admitted this window" and cannot grow without bound under
/// a flood.
/// Fail-closed: a store that cannot answer throws <see cref="RateLimitUnavailableException"/>.
/// </summary>
public sealed class RedisRateLimitCounter : IDisposable
{
    // KEYS[1]=counter key, ARGV[1]=window ms, ARGV[2]=limit
    // -> { count, ttlMs, admitted } (ttl is never -1 for a live window: the
    //    first admitted increment sets it in the same script)
    private const string FixedWindowScript = """
        local limit = tonumber(ARGV[2])
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count >= limit then
            return { count, redis.call('PTTL', KEYS[1]), 0 }
        end
        count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return { count, redis.call('PTTL', KEYS[1]), 1 }
        """;

    private readonly IOptionsMonitor<RateLimitOptions> _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private IConnectionMultiplexer? _mux;
    private string? _connectedTo;

    public RedisRateLimitCounter(IOptionsMonitor<RateLimitOptions> options) => _options = options;

    public async Task<RateLimitDecision> ReserveAsync(
        string bucket, string partition, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        var key = $"{_options.CurrentValue.KeyPrefix}{bucket}:{partition}";
        try
        {
            var result = await db.ScriptEvaluateAsync(
                FixedWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { (long)window.TotalMilliseconds, limit });

            var values = (RedisValue[])result!;
            var count = (long)values[0];
            var ttlMs = (long)values[1];
            var admitted = (long)values[2] == 1;

            if (ttlMs < 0) ttlMs = (long)window.TotalMilliseconds; // defensive: key somehow lost its TTL

            return new RateLimitDecision(
                Allowed: admitted,
                Limit: limit,
                Remaining: admitted ? (int)Math.Max(0, limit - count) : 0,
                WindowRemaining: TimeSpan.FromMilliseconds(Math.Max(0, ttlMs)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RateLimitUnavailableException($"{bucket}: {ex.Message}");
        }
    }

    /// <summary>
    /// Lazy connect, same self-contained pattern as the cache and B2 store.
    /// Re-reads the monitored options so a reloaded connection string takes
    /// effect without a restart.
    /// </summary>
    private async Task<IDatabase> DatabaseAsync(CancellationToken ct)
    {
        var connectionString = _options.CurrentValue.ConnectionString;

        if (_mux is { IsConnected: true } live && _connectedTo == connectionString)
            return live.GetDatabase();

        await _connectGate.WaitAsync(ct);
        try
        {
            if (_mux is null || !_mux.IsConnected || _connectedTo != connectionString)
            {
                var o = _options.CurrentValue;
                var config = ConfigurationOptions.Parse(
                    $"{connectionString},connectTimeout={o.ConnectTimeoutMs},connectRetry=1,abortConnect=true");
                try
                {
                    var fresh = await ConnectionMultiplexer.ConnectAsync(config);
                    var old = Interlocked.Exchange(ref _mux, fresh);
                    _connectedTo = connectionString;
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

        return _mux!.GetDatabase();
    }

    public void Dispose()
    {
        _mux?.Dispose();
        _connectGate.Dispose();
    }
}
