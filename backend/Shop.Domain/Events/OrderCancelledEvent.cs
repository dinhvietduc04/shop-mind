namespace Shop.Domain.Events;

/// <summary>Catalog v1: order.cancelled { orderId, userId, reason? }</summary>
public sealed record OrderCancelledEvent(Guid OrderId, Guid UserId, string? Reason = null) : IDomainEvent
{
    public string EventType => "order.cancelled";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId, reason = Reason };
}
