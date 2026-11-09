using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// </summary>
public class ShopWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ExternalConnectionStringEnvVar = "SHOPMIND_TEST_POSTGRES";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalConnectionString;
    private readonly string? _externalAdminConnectionString;
    private readonly string? _externalDatabaseName;

    public ShopWebFactory()
    {
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
        });
        builder.UseSetting("ApplyMigrations", "false");
        builder.UseSetting("Jwt:Secret", "shopmind-integration-test-secret-32chars!!");
        // M9: integration tests replace ICacheService with an in-memory fake per
        // test; keep the default suite quiet (no Redis connection attempts).
        builder.UseSetting("Redis:Enabled", "false");
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
            return;
        }
        await base.DisposeAsync();
    }
}
