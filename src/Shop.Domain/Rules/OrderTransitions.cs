using Shop.Domain.Enums;

namespace Shop.Domain.Rules;

public static class OrderTransitions
{
    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> Allowed = new()
    {
        [OrderStatus.Pending] = new() { OrderStatus.Paid, OrderStatus.PaymentFailed, OrderStatus.Cancelled },
        [OrderStatus.Paid] = new() { OrderStatus.Processing, OrderStatus.Cancelled },
        [OrderStatus.Processing] = new() { OrderStatus.Shipped },
        [OrderStatus.Shipped] = new() { OrderStatus.Delivered },
        [OrderStatus.Delivered] = new(),
        [OrderStatus.Cancelled] = new(),
        [OrderStatus.PaymentFailed] = new(),
    };

    public static bool IsValidTransition(OrderStatus from, OrderStatus to)
        => Allowed.TryGetValue(from, out var next) && next.Contains(to);
}
