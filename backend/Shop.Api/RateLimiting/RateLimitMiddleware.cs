using Shop.Api.RateLimiting.Interfaces;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.RateLimiting;

namespace Shop.Api.RateLimiting;

public sealed class RateLimitMiddleware(
    RequestDelegate next,
    ILogger<RateLimitMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IRateLimitPolicyResolver resolver,
        IRateLimitEnforcer enforcer,
        IRateLimitResponseWriter responseWriter)
    {
        var policies = resolver.Resolve(context);

        RateLimitDecision? mostRestrictive = null;
        double lowestRemainingRatio = double.MaxValue;

        foreach (var policy in policies)
        {
            RateLimitDecision decision;

            try
            {
                decision = await enforcer.CheckAsync(
                    policy,
                    context.RequestAborted);
            }
            catch (RateLimitUnavailableException ex)
            {
                logger.LogWarning(
                    ex,
                    "Rate limiter unavailable for bucket {Bucket}",
                    policy.Bucket);

                await responseWriter.WriteUnavailableAsync(context);
                return;
            }

            if (!decision.Allowed)
            {
                logger.LogInformation(
                    "Rate limited {Bucket} ({Partition})",
                    policy.Bucket,
                    policy.Partition);

                responseWriter.WriteHeaders(
                    context.Response,
                    decision);

                await responseWriter.WriteDeniedAsync(
                    context,
                    decision);

                return;
            }

            var ratio = decision.Limit > 0
                ? (double)decision.Remaining / decision.Limit
                : 0;

            if (ratio < lowestRemainingRatio)
            {
                lowestRemainingRatio = ratio;
                mostRestrictive = decision;
            }
        }

        if (mostRestrictive is not null)
        {
            responseWriter.WriteHeaders(
                context.Response,
                mostRestrictive);
        }

        await next(context);
    }
}
