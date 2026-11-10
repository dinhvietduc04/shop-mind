namespace Shop.Domain.Events;

/// <summary>Catalog v1: order.shipped { orderId, userId }</summary>
public sealed record OrderShippedEvent(Guid OrderId, Guid UserId) : IDomainEvent
{
    public string EventType => "order.shipped";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId };
}
