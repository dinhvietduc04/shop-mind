using System.Text.Json;
using System.Text.Json.Serialization;
using Shop.Application.Common;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Events;

namespace Shop.Application.Services;

/// <summary>
/// M10: composes the locked payload contract v1 and adds the outbox row to the
/// ambient transaction. Never SaveChanges itself — the caller's commit is atomic
/// across business rows and events (dual-write is forbidden by the plan).
/// </summary>
public class EventPublisher(IAppDbContext db) : IEventPublisher
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task EnqueueAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var message = new OutboxMessage
        {
            AggregateType = domainEvent.AggregateType,
            AggregateId = domainEvent.AggregateId,
            Type = domainEvent.EventType,
            OccurredAt = DateTime.UtcNow,
        };

        // eventId == outbox row id: the M11 publisher reuses it as broker messageId
        // so consumers can dedupe by (EventId) exactly like webhooks.
        message.Payload = JsonSerializer.Serialize(new
        {
            eventId = message.Id,
            eventType = message.Type,
            occurredAt = message.OccurredAt,
            aggregateType = message.AggregateType,
            aggregateId = message.AggregateId,
            userId = domainEvent.UserId,
            correlationId = CorrelationContext.Id,
            data = domainEvent.Data,
            schemaVersion = 1,
        }, Json);

        await db.OutboxMessages.AddAsync(message, ct);
    }
}
