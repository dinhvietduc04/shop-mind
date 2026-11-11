using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shop.Application.Interfaces;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Seed;
using Shop.Mcp;
using Testcontainers.PostgreSql;

namespace Shop.IntegrationTests;

/// <summary>
/// A1: boots the real Shop.Mcp HTTP host (Streamable HTTP at /mcp) against a
/// disposable Postgres database and the shared test Redis. Mirrors the
/// ShopWebFactory database seam so MCP and REST tests use identical seed data.
/// </summary>
public class McpWebFactory : WebApplicationFactory<McpProgram>, IAsyncLifetime
{
    public const string TestJwtSecret = "shopmind-integration-test-secret-32chars!!";
    private const string ExternalConnectionStringEnvVar = "SHOPMIND_TEST_POSTGRES";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalAdminConnectionString;
    private readonly string? _externalDatabaseName;
    private readonly string? _externalConnectionString;

    public TestRedis Redis { get; }

    public int ReadPerMin { get; init; } = 100_000;
    public int WritePerMin { get; init; } = 100_000;
    public int QuotePerMin { get; init; } = 100_000;

    /// <summary>Override to fail only the idempotency store while budgets stay live.</summary>
    protected virtual string IdempotencyConnectionString => Redis.ConnectionString;

    public McpWebFactory() : this(new TestRedis()) { }

    protected McpWebFactory(TestRedis redis)
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

        _externalDatabaseName = $"shopmind_mcp_test_{Guid.NewGuid():N}";
        _externalAdminConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" }.ToString();
        using (var conn = new NpgsqlConnection(_externalAdminConnectionString))
        {
            conn.Open();
            using var cmd = new NpgsqlCommand($"CREATE DATABASE {_externalDatabaseName}", conn);
            cmd.ExecuteNonQuery();
        }
        _externalConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _externalDatabaseName }.ToString();
    }

    public string DatabaseConnectionString => _container?.GetConnectionString() ?? _externalConnectionString!;

    public async Task InitializeAsync()
    {
        if (_container is not null) await _container.StartAsync();
        await Redis.InitializeAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await SeedData.EnsureSeededAsync(db, hasher);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", DatabaseConnectionString);
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("Jwt:Issuer", "ShopMind");
        builder.UseSetting("Mcp:Audience", "Shop.Mcp");
        builder.UseSetting("Redis:Enabled", "false");
        builder.UseSetting("Redis:ConnectionString", Redis.ConnectionString);
        builder.UseSetting("Mcp:KeyPrefix", Redis.KeyPrefix + "mcp:");
        builder.UseSetting("Idempotency:ConnectionString", IdempotencyConnectionString);
        builder.UseSetting("Idempotency:KeyPrefix", Redis.KeyPrefix + "idem:");
        builder.UseSetting("Mcp:ReadPerMin", ReadPerMin.ToString());
        builder.UseSetting("Mcp:WritePerMin", WritePerMin.ToString());
        builder.UseSetting("Mcp:QuotePerMin", QuotePerMin.ToString());
        builder.UseSetting("Mcp:WindowSeconds", "60");
        builder.UseSetting("Logging:LogLevel:Default", "Information");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Warning");

        builder.ConfigureServices(services =>
            services.AddLogging(logging => logging.AddProvider(TestLogSink.Instance)));
    }

    public new async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
        else
        {
            await base.DisposeAsync();
            if (_externalAdminConnectionString is not null && _externalDatabaseName is not null)
            {
                using var conn = new NpgsqlConnection(_externalAdminConnectionString);
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS {_externalDatabaseName} WITH (FORCE)", conn);
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
/// A2 fail-closed proof: budgets stay live, but the idempotency store can never
/// answer. A WRITE/QUOTE tool must refuse before executing, never run
/// un-deduplicated.
/// </summary>
public sealed class DeadRedisMcpFactory : McpWebFactory
{
    protected override string IdempotencyConnectionString => "127.0.0.1:1";
}
