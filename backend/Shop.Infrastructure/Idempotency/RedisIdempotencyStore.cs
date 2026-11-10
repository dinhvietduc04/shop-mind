using System.Text.Json;
using System.Text.Json.Serialization;
using Shop.Application.Interfaces;
using Shop.Domain.Exceptions;
using StackExchange.Redis;

namespace Shop.Infrastructure.Idempotency;

/// <summary>
/// B2 (ADR-0004): Redis-backed renewing-lease idempotency store (ADR-0004).
/// One JSON record per key; the physical TTL IS the lease while InProgress
/// (SET NX, holder-only compare-and-renew) and becomes the replay retention
/// window on the owner-checked CAS transition to Done.
/// Unlike the M9 cache this store FAILS CLOSED: any connectivity problem is an
/// <see cref="IdempotencyUnavailableException"/>, never a silent pass-through.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IdempotencyOptions _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private IConnectionMultiplexer? _mux;

    // KEYS[1]=record key, ARGV[1]=lease token, ARGV[2]=lease ms
    private const string RenewScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local ok, rec = pcall(cjson.decode, raw)
        if not ok then return 0 end
        if rec.state ~= 'InProgress' or rec.leaseHolderToken ~= ARGV[1] then return 0 end
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return 1
        """;

    // KEYS[1]=record key, ARGV[1]=lease token, ARGV[2]=Done retention ms, ARGV[3]=Done record JSON
    private const string CompleteScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local ok, rec = pcall(cjson.decode, raw)
        if not ok then return 0 end
        if rec.state ~= 'InProgress' or rec.leaseHolderToken ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[2])
        return 1
        """;

    // KEYS[1]=record key, ARGV[1]=lease token
    private const string ReleaseScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local ok, rec = pcall(cjson.decode, raw)
        if not ok then return 0 end
        if rec.state ~= 'InProgress' or rec.leaseHolderToken ~= ARGV[1] then return 0 end
        redis.call('DEL', KEYS[1])
        return 1
        """;

    public RedisIdempotencyStore(IdempotencyOptions options) => _options = options;

    public async Task<IdempotencyAcquisition> TryAcquireAsync(string key, string requestHash, CancellationToken ct = default)
    {
        // At most two passes: the second covers the narrow race where the previous
        // lease expires between our failed SET NX and our read of it.
        for (var attempt = 0; ; attempt++)
        {
            var db = await DatabaseAsync(ct);
            var fullKey = Full(key);
            var token = Guid.NewGuid().ToString("N");
            var record = new IdempotencyRecord(token, requestHash, IdempotencyState.InProgress, DateTimeOffset.UtcNow);

            bool created;
            try
            {
                created = await db.StringSetAsync(
                    fullKey, JsonSerializer.Serialize(record, Json),
                    TimeSpan.FromSeconds(_options.LeaseSeconds), When.NotExists);
            }
            catch (Exception ex) { throw Translate(ex, "SET-NX"); }

            if (created)
                return new IdempotencyAcquisition(IdempotencyAcquisitionResult.Acquired, LeaseToken: token);

            IdempotencyRecord? existing;
            TimeSpan? ttl;
            try
            {
                existing = await GetRawAsync(db, fullKey);
                ttl = await db.KeyTimeToLiveAsync(fullKey);
            }
            catch (Exception ex) { throw Translate(ex, "GET"); }

            if (existing is null && attempt == 0)
                continue; // lease lapsed in-flight: fall through and re-acquire

            if (existing is null)
                throw new IdempotencyUnavailableException("lease vanished between acquire attempts");

            if (existing.State == IdempotencyState.Done)
                return existing.RequestHash == requestHash
                    ? new IdempotencyAcquisition(IdempotencyAcquisitionResult.Replay, StoredResponse: existing.Response)
                    : new IdempotencyAcquisition(IdempotencyAcquisitionResult.PayloadMismatch);

            var retryAfter = ttl is null
                ? _options.LeaseSeconds
                : Math.Max(1, (int)Math.Ceiling(ttl.Value.TotalSeconds));
            return new IdempotencyAcquisition(IdempotencyAcquisitionResult.InProgress, RetryAfterSeconds: retryAfter);
        }
    }

    public async Task<bool> RenewLeaseAsync(string key, string leaseToken, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        try
        {
            // Compare-and-renew: only the recorded holder may extend. A stolen or
            // already-Done key is left untouched.
            var result = await db.ScriptEvaluateAsync(
                RenewScript,
                new RedisKey[] { Full(key) },
                new RedisValue[] { leaseToken, TimeSpan.FromSeconds(_options.LeaseSeconds).TotalMilliseconds });
            return (long)result == 1;
        }
        catch (Exception ex) { throw Translate(ex, "RENEW"); }
    }

    public async Task<bool> CompleteAsync(string key, string leaseToken, StoredIdempotentResponse response, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        var fullKey = Full(key);
        try
        {
            var current = await GetRawAsync(db, fullKey);
            if (current is null
                || current.State != IdempotencyState.InProgress
                || current.LeaseHolderToken != leaseToken)
                return false;

            var done = current with { State = IdempotencyState.Done, Response = response };

            // Owner-checked CAS: the script re-verifies holder+state at write time,
            // so a lease stolen after our read cannot be overwritten by a zombie.
            var result = await db.ScriptEvaluateAsync(
                CompleteScript,
                new RedisKey[] { fullKey },
                new RedisValue[]
                {
                    leaseToken,
                    TimeSpan.FromSeconds(_options.DoneRetentionSeconds).TotalMilliseconds,
                    JsonSerializer.Serialize(done, Json),
                });
            return (long)result == 1;
        }
        catch (Exception ex) { throw Translate(ex, "COMPLETE"); }
    }

    public async Task<bool> ReleaseAsync(string key, string leaseToken, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        try
        {
            // Owner-checked delete so an errored attempt frees the key at once
            // without ever touching another holder's lease.
            var result = await db.ScriptEvaluateAsync(
                ReleaseScript,
                new RedisKey[] { Full(key) },
                new RedisValue[] { leaseToken });
            return (long)result == 1;
        }
        catch (Exception ex) { throw Translate(ex, "RELEASE"); }
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
    {
        var db = await DatabaseAsync(ct);
        try
        {
            return await GetRawAsync(db, Full(key));
        }
        catch (Exception ex) { throw Translate(ex, "GET"); }
    }

    private string Full(string key) => _options.KeyPrefix + key;

    private static async Task<IdempotencyRecord?> GetRawAsync(IDatabase db, string fullKey)
    {
        var raw = await db.StringGetAsync(fullKey);
        return raw.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<IdempotencyRecord>((string)raw!, Json);
    }

    /// <summary>Lazy connect on the request path; a broken server surfaces as fail-closed.</summary>
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
                        throw new IdempotencyUnavailableException(ex.Message);
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

    private static Exception Translate(Exception ex, string op)
        => ex is RedisConnectionException or RedisTimeoutException or InvalidOperationException
            ? new IdempotencyUnavailableException($"{op}: {ex.Message}")
            : ex;

    public void Dispose()
    {
        _mux?.Dispose();
        _connectGate.Dispose();
    }
}
