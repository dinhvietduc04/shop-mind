using Shop.Api.RateLimiting.Models;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting.Interfaces;

public interface IRateLimitEnforcer
{
    Task<RateLimitDecision> CheckAsync(
        RateLimitPolicy policy,
        CancellationToken cancellationToken
    );
}
