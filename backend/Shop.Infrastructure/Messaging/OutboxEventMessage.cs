namespace Shop.Infrastructure.Messaging;

/// <summary>
/// B4: a broker message normalized from the M10 payload contract v1.
/// EventId == the outbox row id (broker messageId), so redeliveries and
/// duplicates are recognizable across the whole chain.
/// </summary>
public sealed record OutboxEventMessage(Guid EventId, string EventType, string PayloadJson);
