using StackExchange.Redis;
using Testcontainers.Redis;

namespace Shop.IntegrationTests;

/// <summary>
/// Redis for integration tests comes from one of two places (mirrors the
/// Postgres seam in <see cref="ShopWebFactory"/>):
/// - default (CI): Testcontainers spins redis:7-alpine (needs a container runtime);
/// - local: set SHOPMIND_TEST_REDIS to a throwaway server (e.g. "127.0.0.1:6379").
/// Every instance owns a unique key prefix; disposal deletes ONLY that prefix's
/// keys — never FLUSHALL — so the server may safely host dev data.
/// </summary>
public sealed class TestRedis
{
    public const string ConnectionStringEnvVar = "SHOPMIND_TEST_REDIS";

    public string KeyPrefix { get; } = $"shopmindtest:{Guid.NewGuid():N}:";
    public string ConnectionString { get; private set; }

    private readonly RedisContainer? _container;
    private readonly bool _isSharedServer;

    public TestRedis()
    {
        var external = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            _isSharedServer = true;
            return;
        }
        _container = new RedisBuilder().WithImage("redis:7-alpine").Build();
        ConnectionString = null!;
    }

    private TestRedis(string connectionString)
    {
        ConnectionString = connectionString;
        _isSharedServer = false; // nothing to purge: it never answers
    }

    /// <summary>A connection string that cannot serve — for fail-closed tests (ADR-0004).</summary>
    public static TestRedis Unreachable(string connectionString) => new(connectionString);

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
            return;
        }
        if (_isSharedServer)
            await PurgeOwnKeysAsync();
    }

    private async Task PurgeOwnKeysAsync()
    {
        try
        {
            using var mux = await ConnectionMultiplexer.ConnectAsync(
                $"{ConnectionString},connectTimeout=2000,abortConnect=false");
            var db = mux.GetDatabase();
            foreach (var endpoint in mux.GetEndPoints())
            {
                var server = mux.GetServer(endpoint);
                await foreach (var key in server.KeysAsync(pattern: KeyPrefix + "*", pageSize: 200).ConfigureAwait(false))
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort: keys are lease-scoped and expire on their own anyway.
        }
    }
}
