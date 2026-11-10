namespace Shop.Domain.Events;

/// <summary>Catalog v1: payment.failed { orderId, userId, reason }</summary>
public sealed record PaymentFailedEvent(Guid OrderId, Guid UserId, string Reason) : IDomainEvent
{
    public string EventType => "payment.failed";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    Guid? IDomainEvent.UserId => UserId;
    public object Data => new { orderId = OrderId, userId = UserId, reason = Reason };
}
