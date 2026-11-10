namespace Shop.Domain.Exceptions;

/// <summary>
/// B3 / ADR-0004 fail-closed: the limiter's Redis store cannot answer, so the
/// request is denied instead of running un-throttled.
/// </summary>
public class RateLimitUnavailableException : ShopDomainException
{
    public RateLimitUnavailableException(string reason)
        : base("RateLimitUnavailable",
            $"Rate limiter is unavailable; the request was NOT executed. Retry later. ({reason})") { }
}
