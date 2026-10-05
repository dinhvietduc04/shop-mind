using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Interfaces;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Seed;
using Testcontainers.PostgreSql;

namespace Shop.IntegrationTests;

public class ShopWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("shopmind_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
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

            // Use testcontainer connection string
            var conn = _db.GetConnectionString();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));
        });
        builder.UseSetting("ApplyMigrations", "false");
        builder.UseSetting("Jwt:Secret", "shopmind-integration-test-secret-32chars!!");
    }

    public new async Task DisposeAsync()
    {
        await _db.StopAsync();
        await base.DisposeAsync();
    }
}
