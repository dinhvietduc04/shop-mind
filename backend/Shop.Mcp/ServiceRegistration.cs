using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Shop.Application.Caching;
using Shop.Application.Mcp;
using Shop.Infrastructure;
using Shop.Infrastructure.RateLimiting;
using Shop.Mcp.Auth;
using Shop.Mcp.Tooling;

namespace Shop.Mcp;

/// <summary>
/// A1 composition root shared by the HTTP and STDIO hosts. The MCP project gets
/// Application services through the same DI registration as the API; tool code
/// never touches the database directly.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddShopMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);
        services.Configure<McpOptions>(configuration.GetSection("Mcp"));

        services.AddSingleton<IToolRiskPolicy>(ToolRiskPolicy.Default);
        services.AddHttpContextAccessor();
        services.AddScoped<McpToolContextAccessor>();
        services.AddScoped<McpToolContext>(sp => sp.GetRequiredService<McpToolContextAccessor>().Current);

        services.AddSingleton<IMcpBudgetCounter>(sp =>
        {
            var mcp = sp.GetRequiredService<IOptions<McpOptions>>().Value;
            return new RedisMcpBudgetCounter(
                mcp,
                sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString,
                mcp.KeyPrefix);
        });

        services.AddScoped<ToolExecutor>();
        services.AddSingleton<IPostConfigureOptions<McpServerOptions>, ToolManifestPostConfigure>();

        return services;
    }
}
