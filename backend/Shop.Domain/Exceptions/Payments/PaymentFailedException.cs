namespace Shop.Domain.Exceptions;

public class PaymentFailedException : ShopDomainException
{
    public Guid OrderId { get; }
    public PaymentFailedException(Guid orderId, string? reason = null)
        : base("PaymentFailed", string.IsNullOrWhiteSpace(reason) ? $"Payment failed for order '{orderId}'." : reason)
    {
        OrderId = orderId;
    }
}
