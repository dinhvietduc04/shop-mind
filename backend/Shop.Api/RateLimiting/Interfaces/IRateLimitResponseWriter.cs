using Microsoft.AspNetCore.Http;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting.Interfaces;

public interface IRateLimitResponseWriter
{
    void WriteHeaders(
        HttpResponse response,
        RateLimitDecision decision
    );

    Task WriteDeniedAsync(
        HttpContext context,
        RateLimitDecision decision
    );

    Task WriteUnavailableAsync(HttpContext context);
}