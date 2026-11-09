namespace Shop.Domain.Entities;

/// <summary>
/// M10/B4: consumer-side idempotency ledger (plan: ProcessedEvents(EventId PK)
/// for webhooks/queue consumers). Keyed per (EventId, Consumer): every consumer
/// gets its own exactly-once slot for the same event.
/// </summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
