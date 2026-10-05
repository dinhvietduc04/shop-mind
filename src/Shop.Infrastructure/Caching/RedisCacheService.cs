using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Application.Caching;
using StackExchange.Redis;

namespace Shop.Infrastructure.Caching;

/// <summary>
/// M9: Redis-backed <see cref="ICacheService"/>.
/// Resilience: cache failures never fail the request — log (throttled to
/// once/minute) and fall through to the database. Honors the
/// <c>Redis:Enabled</c> kill-switch via <see cref="IOptionsMonitor{T}"/>.
/// </summary>
public sealed class RedisCacheService : ICacheService, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan ErrorLogThrottle = TimeSpan.FromMinutes(1);

    private readonly IOptionsMonitor<RedisOptions> _options;
    private readonly ILogger<RedisCacheService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private IConnectionMultiplexer? _mux;
    private long _lastErrorTicks;
    private long _lastConnectAttemptTicks;
    private int _connecting;
    private static readonly TimeSpan ConnectCooldown = TimeSpan.FromSeconds(5);

    public RedisCacheService(IOptionsMonitor<RedisOptions> options, ILogger<RedisCacheService> log)
    {
        _options = options;
        _log = log;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var db = await GetDbAsync().ConfigureAwait(false);        if (db is null) return default;
        try
        {
            var raw = await db.StringGetAsync(key).ConfigureAwait(false);
            if (raw.IsNullOrEmpty) return default;
            return JsonSerializer.Deserialize<T>((string)raw!, Json);
        }
        catch (Exception ex)
        {
            LogOnce(ex, "GET", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var db = await GetDbAsync().ConfigureAwait(false);
        if (db is null || value is null) return;
        try
        {
            var raw = JsonSerializer.Serialize(value, Json);
            await db.StringSetAsync(key, raw, ttl ?? DefaultTtl()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogOnce(ex, "SET", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var db = await GetDbAsync().ConfigureAwait(false);
        if (db is null) return;
        try
        {
            await db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogOnce(ex, "DEL", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var mux = GetMux();
        if (mux is null) return;
        try
        {
            var db = mux.GetDatabase();
            foreach (var endpoint in mux.GetEndPoints())
            {
                var server = mux.GetServer(endpoint);
                await foreach (var key in server.KeysAsync(pattern: prefix + "*", pageSize: 200).WithCancellation(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogOnce(ex, "DEL-prefix-key", key.ToString());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce(ex, "DEL-prefix", prefix);
        }
    }

    public async Task<(T Value, bool FromCache)> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct).ConfigureAwait(false);
        if (cached is not null) return (cached, true);

        // Single-flight per key: only one caller in this instance hits the DB on a miss.
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cached = await GetAsync<T>(key, ct).ConfigureAwait(false);
            if (cached is not null) return (cached, true);

            var value = await factory().ConfigureAwait(false);
            if (value is not null)
                await SetAsync(key, value, ttl, ct).ConfigureAwait(false);
            return (value, false);
        }
        finally
        {
            gate.Release();
        }
    }

    private TimeSpan DefaultTtl()
    {
        var seconds = _options.CurrentValue.DefaultTtlSeconds;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(90);
    }

    private Task<IDatabase?> GetDbAsync()
    {
        var mux = GetMux();
        if (mux is null) return Task.FromResult<IDatabase?>(null);
        try
        {
            return Task.FromResult<IDatabase?>(mux.GetDatabase());
        }
        catch (Exception ex)
        {
            LogOnce(ex, "GETDB", "-");
            return Task.FromResult<IDatabase?>(null);
        }
    }

    /// <summary>
    /// Never blocks the request path on the network: returns a connected
    /// multiplexer if one is already available, otherwise kicks off a
    /// background connect (at most one per <see cref="ConnectCooldown"/>) and
    /// returns <c>null</c> so the caller falls through to the database.
    /// </summary>
    private IConnectionMultiplexer? GetMux()
    {
        if (!_options.CurrentValue.Enabled) return null;
        var mux = _mux;
        if (mux is { IsConnected: true }) return mux;

        if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastConnectAttemptTicks) < ConnectCooldown.Ticks)
            return null;
        if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
            return null;

        Interlocked.Exchange(ref _lastConnectAttemptTicks, DateTime.UtcNow.Ticks);
        _ = ConnectInBackgroundAsync();
        return null;
    }

    private async Task ConnectInBackgroundAsync()
    {
        try
        {
            var options = ConfigurationOptions.Parse(_options.CurrentValue.ConnectionString);
            options.ConnectTimeout = 2000;
            options.AsyncTimeout = 3000;
            options.AbortOnConnectFail = true;
            var mux = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            var old = Interlocked.Exchange(ref _mux, mux);
            try
            {
                old?.Dispose();
            }
            catch
            {
                // ignore dispose errors from a broken multiplexer
            }
        }
        catch (Exception ex)
        {
            LogOnce(ex, "CONNECT", _options.CurrentValue.ConnectionString);
        }
        finally
        {
            Interlocked.Exchange(ref _connecting, 0);
        }
    }

    private void LogOnce(Exception ex, string op, string key)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastErrorTicks);
        if (new DateTime(now, DateTimeKind.Utc) - new DateTime(last, DateTimeKind.Utc) < ErrorLogThrottle)
            return;
        if (Interlocked.CompareExchange(ref _lastErrorTicks, now, last) != last)
            return;
        _log.LogWarning(ex, "Redis {Op} failed for key {Key}; falling through to DB.", op, key);
    }

    public void Dispose()
    {
        try
        {
            _mux?.Dispose();
        }
        catch
        {
            // ignore on shutdown
        }
        foreach (var gate in _locks.Values)
            gate.Dispose();
    }
}
