using Shop.Domain.Enums;

namespace Shop.Domain.Exceptions;

public class InvalidOrderStateException : ShopDomainException
{
    public Guid OrderId { get; }
    public OrderStatus Current { get; }
    public OrderStatus Attempted { get; }
    public InvalidOrderStateException(Guid orderId, OrderStatus current, OrderStatus attempted)
        : base("InvalidOrderState", $"Invalid order state transition from '{current}' to '{attempted}' for order '{orderId}'.")
    {
        OrderId = orderId;
        Current = current;
        Attempted = attempted;
    }
}
