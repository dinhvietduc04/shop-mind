namespace Shop.Domain.Events;

/// <summary>Catalog v1: order.delivered { orderId, userId }</summary>
public sealed record OrderDeliveredEvent(Guid OrderId, Guid UserId) : IDomainEvent
{
    public string EventType => "order.delivered";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId };
}
