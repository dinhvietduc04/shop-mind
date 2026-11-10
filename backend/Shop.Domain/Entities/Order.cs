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

    /// <summary>
    /// B5 (ADR-0001): a Quote IS a Pending order with an expiry. Legacy V1
    /// Pendings have null ExpiresAt and are therefore NOT Open Quotes (excluded
    /// from the sweeper and the replace policy — read-only history).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>B5+: server-stated reason the quote left the money table; set only by webhook/sweeper transitions.</summary>
    public PaymentOutcome? Outcome { get; set; }

    /// <summary>Glossary "Open Quote" shape: Pending with an expiry attached (elapsed or not — callers compare to now).</summary>
    public bool IsQuote => Status == OrderStatus.Pending && ExpiresAt is not null;

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
