using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shop.Application.Agent;
using Shop.Application.Caching;
using Shop.Application.Comparisons;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Application.Persistence;
using Shop.Application.Services;
using Shop.Infrastructure.Agent;
using Shop.Infrastructure.Ai;
using Shop.Infrastructure.Auth;
using Shop.Infrastructure.Caching;
using Shop.Infrastructure.Idempotency;
using Shop.Infrastructure.Payments;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.RateLimiting;

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

        // B3-L1 (ADR-0004): rate limiting is fail-closed too — same Redis, deny on outage.
        services.Configure<RateLimitOptions>(config.GetSection("RateLimit"));
        services.AddSingleton(sp => new RedisRateLimitCounter(
            sp.GetRequiredService<IOptionsMonitor<RateLimitOptions>>()));

        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IToolRiskPolicy>(ToolRiskPolicy.Default);
        services.AddSingleton<IAgentTokenMinter, AgentTokenMinter>();
        services.AddScoped<IAgentToolGateway, DefaultAgentToolGateway>();
        services.AddScoped<AgentLoop>();

        // B5: PSP adapters behind the normalized webhook contract.
        services.Configure<PaymentsOptions>(config.GetSection("Payments"));
        services.AddScoped(sp => new FakePspProvider(
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IOptions<PaymentsOptions>>().Value.FakePspSecret));
        services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<FakePspProvider>());

        // A4: one AI seam. The fake keeps CI network-free; every other provider
        // goes through the same OpenAI-compatible adapter. A missing key fails
        // at call time, never at startup.
        services.Configure<AiOptions>(config.GetSection("Ai"));
        if (string.Equals(config["Ai:Provider"], AiProviderPresets.Fake, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAiProvider, FakeAiProvider>();
        }
        else
        {
            services.AddHttpClient();
            services.AddSingleton<IAiProvider>(sp => new OpenAiCompatibleProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"),
                sp.GetRequiredService<IOptions<AiOptions>>().Value));
        }

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductComparisonBuilder, ProductComparisonBuilder>(); // A2 compare
        services.AddScoped<IRequirementSearchService, RequirementSearchService>(); // M13 requirement search
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IEventPublisher, EventPublisher>(); // M10
        services.AddScoped<OrderStatusTransitioner>(); // B1/B5: shared conditional-transition engine
        services.AddScoped<IQuoteService, QuoteService>(); // B5
        services.AddScoped<ICheckoutPreviewService, CheckoutPreviewService>(); // M13 dry-run bill
        services.AddScoped<QuoteExpirySweeper>(); // B5
        services.AddScoped<PaymentWebhookConsumer>(); // B5

        return services;
    }
}
