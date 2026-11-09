using Shop.Domain.Enums;

namespace Shop.Domain.Entities;

/// <summary>
/// M10: reliable event delivery via the transactional outbox pattern. Written in
/// the SAME transaction as the business rows (no dual-write); only the M11
/// publisher worker reads and marks PublishedAt/Status — never an HTTP request.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }

    /// <summary>Catalog event type, e.g. "order.paid".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Payload contract v1 JSON (eventId, eventType, occurredAt, aggregateType, aggregateId, userId?, data, correlationId?, schemaVersion).</summary>
    public string Payload { get; set; } = "{}";

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
}
