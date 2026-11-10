using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shop.Application.Interfaces;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Seed;
using Testcontainers.PostgreSql;

namespace Shop.IntegrationTests;

/// <summary>
/// Postgres for integration tests comes from one of two places:
/// - default (CI): Testcontainers spins postgres:16-alpine (needs a container runtime);
/// - local: set SHOPMIND_TEST_POSTGRES to an admin connection string
///   (e.g. "Host=localhost;Port=5432;Username=postgres;Password=postgres") and each factory
///   instance creates its own disposable shopmind_test_&lt;guid&gt; database and drops it on dispose.
/// Redis for the B2 idempotency seam comes from SHOPMIND_TEST_REDIS (see <see cref="TestRedis"/>),
/// namespaced to a per-factory key prefix so the shared server's other data is untouchable.
/// </summary>
public class ShopWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ExternalConnectionStringEnvVar = "SHOPMIND_TEST_POSTGRES";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalConnectionString;
    private readonly string? _externalAdminConnectionString;
    private readonly string? _externalDatabaseName;

    public TestRedis Redis { get; }

    public ShopWebFactory() : this(new TestRedis()) { }

    protected ShopWebFactory(TestRedis redis)
    {
        Redis = redis;
        var admin = Environment.GetEnvironmentVariable(ExternalConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(admin))
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("shopmind_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            return;
        }

        _externalDatabaseName = $"shopmind_test_{Guid.NewGuid():N}";
        _externalAdminConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" }.ToString();
        using (var conn = new NpgsqlConnection(_externalAdminConnectionString))
        {
            conn.Open();
            using var cmd = new NpgsqlCommand($"CREATE DATABASE {_externalDatabaseName}", conn);
            cmd.ExecuteNonQuery();
        }
        _externalConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _externalDatabaseName }.ToString();
    }

    private string ConnectionString => _container?.GetConnectionString() ?? _externalConnectionString!;

    public async Task InitializeAsync()
    {
        if (_container is not null) await _container.StartAsync();
        await Redis.InitializeAsync();
        // Ensure DB created via factory's service configuration below
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await SeedData.EnsureSeededAsync(db, hasher);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            var ctxDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AppDbContext));
            if (ctxDescriptor is not null) services.Remove(ctxDescriptor);

            // Use test database connection string
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnectionString));

            // B5: capture log lines for contract assertions (ADR-0002 /pay/* exclusion).
            services.AddLogging(b => b.AddProvider(TestLogSink.Instance));

            // B2: the test assembly contributes the idempotency probe controller.
            services.AddControllers().AddApplicationPart(typeof(IdempotencyProbeController).Assembly);
            services.AddSingleton<ProbeCounter>();
        });
        builder.UseSetting("ApplyMigrations", "false");
        builder.UseSetting("Jwt:Secret", "shopmind-integration-test-secret-32chars!!");
        // M9: integration tests replace ICacheService with an in-memory fake per
        // test; keep the default suite quiet (no Redis connection attempts).
        builder.UseSetting("Redis:Enabled", "false");
        // B2: idempotency is fail-CLOSED and ignores the cache kill-switch; it gets
        // the disposable test Redis with this factory's unique key namespace.
        builder.UseSetting("Idempotency:ConnectionString", Redis.ConnectionString);
        builder.UseSetting("Idempotency:KeyPrefix", Redis.KeyPrefix);
        // B3-L1: buckets default to effectively-unlimited so unrelated suites never
        // trip; dedicated RateLimit* factories open one bucket small. Counter keys
        // live under this factory's namespaced Redis prefix (purged on dispose).
        // Dead-Redis subclasses deliberately point both at the unreachable store.
        builder.UseSetting("RateLimit:ConnectionString", Redis.ConnectionString);
        builder.UseSetting("RateLimit:KeyPrefix", Redis.KeyPrefix + "rl:");
        builder.UseSetting("RateLimit:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimit:AuthPerMinute", "100000");
        builder.UseSetting("RateLimit:CatalogPerMinute", "100000");
        builder.UseSetting("RateLimit:CartWritePerMinute", "100000");
        builder.UseSetting("RateLimit:CheckoutPerMinute", "100000");
    }

    public new async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
        else
        {
            await base.DisposeAsync(); // release pooled connections to the test DB first
            if (_externalAdminConnectionString is not null && _externalDatabaseName is not null)
            {
                using var conn = new NpgsqlConnection(_externalAdminConnectionString);
                conn.Open();
                // FORCE (PG13+) so pooled stragglers cannot block the drop
                using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_externalDatabaseName} WITH (FORCE)", conn);
                cmd.ExecuteNonQuery();
            }
            await Redis.DisposeAsync();
            return;
        }
        await base.DisposeAsync();
        await Redis.DisposeAsync();
    }
}

/// <summary>
/// B2 fail-closed proof: the app wired to a Redis that can never answer
/// (ADR-0004: money paths must refuse with 503, never run un-deduplicated).
/// </summary>
public sealed class DeadRedisWebFactory : ShopWebFactory
{
    public DeadRedisWebFactory()
        : base(TestRedis.Unreachable("127.0.0.1:1"))
    {
    }
}
