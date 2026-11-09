namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// Outcome of one permit check. When <see cref="Allowed"/> is false,
/// <see cref="WindowRemaining"/> is how long until the window resets
/// (the Retry-After source).
/// </summary>
public sealed record RateLimitDecision(
    bool Allowed,
    int Limit,
    int Remaining,
    TimeSpan WindowRemaining);
