namespace Shop.Domain.Events;

/// <summary>
/// M10: domain event carrying the locked event-catalog-v1 shape. Services enqueue
/// these through <c>IEventPublisher</c> inside their own transaction; the payload
/// contract (schemaVersion 1) is composed once by the publisher.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Catalog type, e.g. "order.paid".</summary>
    string EventType { get; }

    /// <summary>"order" | "payment" | "product".</summary>
    string AggregateType { get; }

    Guid AggregateId { get; }

    Guid? UserId { get; }

    /// <summary>Minimal id/snapshot fields only — never PII or secrets (catalog §M10).</summary>
    object Data { get; }
}
