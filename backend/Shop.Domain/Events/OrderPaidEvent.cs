namespace Shop.Domain.Events;

/// <summary>Catalog v1: order.paid { orderId, userId, totalAmount, transactionId }</summary>
public sealed record OrderPaidEvent(Guid OrderId, Guid UserId, decimal TotalAmount, string? TransactionId) : IDomainEvent
{
    public string EventType => "order.paid";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId, totalAmount = TotalAmount, transactionId = TransactionId };
}
