using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.Rules;
using Shop.Domain.ValueObjects;

namespace Shop.Domain.Entities;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }
    public ShippingAddress ShippingAddress { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public Payment? Payment { get; set; }

    public bool IsCancellable => Status is OrderStatus.Pending or OrderStatus.Paid;

    public void TransitionTo(OrderStatus next)
    {
        if (!OrderTransitions.IsValidTransition(Status, next))
            throw new InvalidOrderStateException(Id, Status, next);
        Status = next;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (!IsCancellable)
            throw new InvalidOrderStateException(Id, Status, OrderStatus.Cancelled);
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public static string GenerateOrderNumber()
        => $"SM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
