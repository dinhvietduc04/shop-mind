namespace Shop.Application.Interfaces.Events;

/// <summary>
/// B4: the broker leg of the outbox pipeline. One event = one message with
/// <c>messageId = eventId</c> (the M10 outbox row id doubles as the consumer
/// dedupe key) and the catalog event type as routing key on the topic exchange.
/// Implementations: RabbitMQ (infra) + recording fakes (tests).
/// </summary>
public interface IBrokerPublisher
{
    /// <summary>
    /// Publish with confirm. Throws on any broker fault — the outbox publisher
    /// translates that into "stay Pending, try again" (at-least-once; consumers dedupe).
    /// </summary>
    Task PublishAsync(string routingKey, Guid messageId, string payloadJson, CancellationToken ct = default);
}
