using Microsoft.Extensions.DependencyInjection;
using Shop.Api.RateLimiting.Interfaces;
using Shop.Api.RateLimiting.Resolvers;
using Shop.Api.RateLimiting.Services;

namespace Shop.Api.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services)
    {
        services.AddScoped<
            IRateLimitPolicyResolver,
            RateLimitPolicyResolver>();

        services.AddScoped<
            IRateLimitEnforcer,
            RedisRateLimitEnforcer>();

        services.AddScoped<
            IRateLimitResponseWriter,
            RateLimitResponseWriter>();

        return services;
    }
}