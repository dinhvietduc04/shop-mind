using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shop.Application.Caching;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Application.Services;
using Shop.Infrastructure.Auth;
using Shop.Infrastructure.Caching;
using Shop.Infrastructure.Idempotency;
using Shop.Infrastructure.Payments;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=shopmind;Username=postgres;Password=postgres";
        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(conn));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<RedisOptions>(config.GetSection("Redis"));
        services.AddSingleton<ICacheService, RedisCacheService>();

        // B2 (ADR-0004): idempotency is fail-closed and independent of the cache
        // kill-switch — money endpoints refuse to run when the store can't answer.
        services.Configure<IdempotencyOptions>(config.GetSection("Idempotency"));
        services.AddSingleton<IIdempotencyStore>(sp => new RedisIdempotencyStore(
            sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value));

        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IFakePaymentService, FakePaymentService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInventoryService, InventoryService>();

        return services;
    }
}
