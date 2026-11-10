using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting;

/// <summary>
/// B3-L1 (plan §B3 Layer 1): the bucket table enforced as one Redis-fixed-window
/// check per request, in pipeline order:
///
///   global        every request            per IP        (default 300/min)
///   auth          POST /api/auth/*         per IP        (10/min — credential stuffing)
///   catalog       GET /api/products|categories            (120/min per user-or-IP)
///   cart-write    /api/cart non-GET        per user      (30/min)
///   checkout      POST /api/checkout*      per user      (6/min; covers B5's /quote)
///
/// Denied: 429 + Retry-After + X-RateLimit-{Limit,Remaining,Reset}.
/// Fail-CLOSED (ADR-0004): Redis unreachable → 503 + Retry-After, handler never runs.
/// The agent-chat bucket (10/min + 100/day per user) joins this table at A5.
/// scope=agent REST rejection is auth-level (ADR-0006), not a bucket.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    RedisRateLimitCounter counter,
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var o = options.Value;
        var window = TimeSpan.FromSeconds(Math.Max(1, o.WindowSeconds));
        var request = context.Request;

        var ip = "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        var userOrIp = userId is not null ? "u:" + userId : ip;

        var path = request.Path;
        var isPost = HttpMethods.IsPost(request.Method);

        var checks = new List<(string Bucket, int Limit, string Partition)>(2)
        {
            ("global", o.GlobalPerMinute, ip),
        };

        if (isPost && path.StartsWithSegments("/api/auth"))
            checks.Add(("auth", o.AuthPerMinute, ip));
        else if (HttpMethods.IsGet(request.Method) &&
                 (path.StartsWithSegments("/api/products") || path.StartsWithSegments("/api/categories")))
            checks.Add(("catalog", o.CatalogPerMinute, userOrIp));
        else if (!HttpMethods.IsGet(request.Method) && path.StartsWithSegments("/api/cart"))
            checks.Add(("cart-write", o.CartWritePerMinute, userOrIp));
        else if (isPost && path.StartsWithSegments("/api/checkout"))
            checks.Add(("checkout", o.CheckoutPerMinute, userOrIp));
        // A5 adds: else if (isPost && path == "/api/agent/chat") → minute + daily counters

        foreach (var (bucket, limit, partition) in checks)
        {
            RateLimitDecision decision;
            try
            {
                decision = await counter.ReserveAsync(bucket, partition, limit, window, context.RequestAborted);
            }
            catch (RateLimitUnavailableException ex)
            {
                log.LogWarning("Rate limiter unavailable for {Bucket}: {Reason}", bucket, ex.Message);
                context.Response.Headers["Retry-After"] = "5";
                await WriteAsync(context, 503, "RateLimitUnavailable",
                    "Rate limiting service is unavailable; the request was NOT executed.");
                return;
            }

            if (!decision.Allowed)
            {
                var retrySeconds = Math.Max(1, (int)Math.Ceiling(decision.WindowRemaining.TotalSeconds));
                log.LogInformation("Rate limited {Bucket} ({Partition}): retry in {Retry}s", bucket, partition, retrySeconds);
                SetHeaders(context.Response, decision, retrySeconds);
                context.Response.Headers["Retry-After"] = retrySeconds.ToString();
                await WriteAsync(context, 429, "RateLimited",
                    $"Rate limit exceeded ({decision.Limit}/min). Try again in {retrySeconds}s.");
                return;
            }

            SetHeaders(context.Response, decision, retrySeconds: null); // strictest bucket wins (last write)
        }

        await next(context);
    }

    private static void SetHeaders(HttpResponse response, RateLimitDecision d, int? retrySeconds)
    {
        response.Headers["X-RateLimit-Limit"] = d.Limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = d.Remaining.ToString();
        response.Headers["X-RateLimit-Reset"] = Math.Max(0, (int)Math.Ceiling(d.WindowRemaining.TotalSeconds)).ToString();
        _ = retrySeconds; // Retry-After is written by the caller on denial
    }

    private static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = status;
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { title = message, status, code }));
    }
}
