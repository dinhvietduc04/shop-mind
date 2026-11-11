using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Shop.Api.RateLimiting.Interfaces;
using Shop.Api.RateLimiting.Models;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting.Resolvers;

public sealed class RateLimitPolicyResolver(
    IOptionsMonitor<RateLimitOptions> options
) : IRateLimitPolicyResolver
{
    public IReadOnlyList<RateLimitPolicy> Resolve(HttpContext context)
    {
        // Monitored options: a reloaded limit applies to the next request.
        var o = options.CurrentValue;
        var request = context.Request;

        var window = TimeSpan.FromSeconds(
            Math.Max(1, o.WindowSeconds));

        var ip = GetIpPartition(context);

        var userId = context.User.FindFirstValue(
            ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        var userPartition = userId is not null
            ? $"u:{userId}"
            : ip;

        var policies = new List<RateLimitPolicy>(2);

        Add(policies, "global", o.GlobalPerMinute, window, ip);

        if (IsAuthRequest(request))
            Add(policies, "auth", o.AuthPerMinute, window, ip);
        else if (IsCatalogRequest(request))
            Add(policies, "catalog", o.CatalogPerMinute, window, userPartition);
        else if (IsCartWriteRequest(request))
            Add(policies, "cart-write", o.CartWritePerMinute, window, userPartition);
        else if (IsCheckoutRequest(request))
            Add(policies, "checkout", o.CheckoutPerMinute, window, userPartition);

        return policies;
    }

    /// <summary>
    /// A non-positive limit disables the bucket entirely: no Redis round-trip,
    /// no X-RateLimit-* headers, nothing to trip. (A limit of 0 used to mean
    /// "deny everything", which turned a config typo into a total outage.)
    /// </summary>
    private static void Add(
        List<RateLimitPolicy> policies,
        string bucket,
        int limit,
        TimeSpan window,
        string partition)
    {
        if (limit <= 0)
            return;

        policies.Add(Create(bucket, limit, window, partition));
    }

    private static RateLimitPolicy Create(
        string bucket,
        int limit,
        TimeSpan window,
        string partition)
    {
        return new RateLimitPolicy(
            bucket,
            limit,
            window,
            partition);
    }

    private static string GetIpPartition(HttpContext context)
    {
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static bool IsAuthRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.StartsWithSegments("/api/auth");

    private static bool IsCatalogRequest(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && (
            request.Path.StartsWithSegments("/api/products")
            || request.Path.StartsWithSegments("/api/categories")
        );

    private static bool IsCartWriteRequest(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && request.Path.StartsWithSegments("/api/cart");

    private static bool IsCheckoutRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.StartsWithSegments("/api/checkout");
}
