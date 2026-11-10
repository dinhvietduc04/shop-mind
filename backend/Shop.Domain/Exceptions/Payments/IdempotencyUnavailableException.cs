namespace Shop.Domain.Exceptions;

public class IdempotencyUnavailableException : ShopDomainException
{
    public IdempotencyUnavailableException(string reason)
        : base("IdempotencyUnavailable",
            $"Idempotency store is unavailable; the request was NOT executed. Retry later. ({reason})") { }
}
