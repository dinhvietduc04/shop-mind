using Shop.Api.RateLimiting.Interfaces;
using Shop.Api.RateLimiting.Models;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting.Services;

public sealed class RedisRateLimitEnforcer(
    RedisRateLimitCounter counter
) : IRateLimitEnforcer
{
    public Task<RateLimitDecision> CheckAsync(
        RateLimitPolicy policy,
        CancellationToken cancellationToken
    )
    {
        return counter.ReserveAsync(
            policy.Bucket,
            policy.Partition,
            policy.Limit,
            policy.Window,
            cancellationToken
        );
    }
}