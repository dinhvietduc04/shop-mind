using System.Globalization;
using Microsoft.AspNetCore.Http;
using Shop.Api.RateLimiting.Interfaces;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting.Services;

/// <summary>
/// Two header families, each with its conventional meaning:
/// <list type="bullet">
/// <item><c>X-RateLimit-Limit/Remaining/Reset</c> — the de-facto GitHub/Twitter
/// contract, where <c>Reset</c> is a <b>UTC epoch second</b>.</item>
/// <item><c>RateLimit-Limit/Remaining/Reset</c> — IETF
/// draft-ietf-httpapi-ratelimit-headers, where <c>Reset</c> is <b>delta
/// seconds</b>. <c>Retry-After</c> is the same delta on refusals.</item>
/// </list>
/// Emitting both removes the "is X-RateLimit-Reset an epoch or a countdown?"
/// ambiguity that made clients misread the old single-family header.
/// </summary>
public sealed class RateLimitResponseWriter
    : IRateLimitResponseWriter
{
    public void WriteHeaders(
        HttpResponse response,
        RateLimitDecision decision)
    {
        var retrySeconds = GetRetrySeconds(decision);
        var resetEpoch = DateTimeOffset.UtcNow
            .AddSeconds(retrySeconds)
            .ToUnixTimeSeconds();

        response.Headers["X-RateLimit-Limit"] =
            Invariant(decision.Limit);

        response.Headers["X-RateLimit-Remaining"] =
            Invariant(decision.Remaining);

        response.Headers["X-RateLimit-Reset"] =
            Invariant(resetEpoch);

        response.Headers["RateLimit-Limit"] =
            Invariant(decision.Limit);

        response.Headers["RateLimit-Remaining"] =
            Invariant(decision.Remaining);

        response.Headers["RateLimit-Reset"] =
            Invariant(retrySeconds);
    }

    public async Task WriteDeniedAsync(
        HttpContext context,
        RateLimitDecision decision)
    {
        var retrySeconds = GetRetrySeconds(decision);

        context.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;

        context.Response.Headers.RetryAfter =
            Invariant(retrySeconds);

        await context.Response.WriteAsJsonAsync(new
        {
            title = $"Rate limit exceeded ({decision.Limit}/window).",
            status = StatusCodes.Status429TooManyRequests,
            code = "RateLimited",
            retryAfter = retrySeconds
        }, context.RequestAborted);
    }

    public async Task WriteUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode =
            StatusCodes.Status503ServiceUnavailable;

        context.Response.Headers.RetryAfter = "5";

        await context.Response.WriteAsJsonAsync(new
        {
            title = "Rate limiting service is unavailable; the request was NOT executed.",
            status = StatusCodes.Status503ServiceUnavailable,
            code = "RateLimitUnavailable"
        }, context.RequestAborted);
    }

    private static int GetRetrySeconds(RateLimitDecision decision)
    {
        return Math.Max(
            1,
            (int)Math.Ceiling(
                decision.WindowRemaining.TotalSeconds));
    }

    // Culture-invariant: header values must stay ASCII digits under any locale.
    private static string Invariant(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
