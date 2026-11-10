namespace Shop.Domain.Events;

/// <summary>Catalog v1: order.created { orderId, userId, totalAmount, itemCount }</summary>
public sealed record OrderCreatedEvent(Guid OrderId, Guid UserId, decimal TotalAmount, int ItemCount) : IDomainEvent
{
    public string EventType => "order.created";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId, totalAmount = TotalAmount, itemCount = ItemCount };
}
