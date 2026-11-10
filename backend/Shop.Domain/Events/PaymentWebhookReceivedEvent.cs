namespace Shop.Domain.Events;

/// <summary>
/// B5 internal catalog extension: a verified provider webhook normalized and
/// enqueued for the worker (payment.webhook-received). The worker consumer
/// applies the money fate matrix; the intake path itself only verifies + enqueues.
/// </summary>
public sealed record PaymentWebhookReceivedEvent(
    Guid DeterministicEventId,
    string Provider,
    string ProviderEventId,
    string SessionToken,
    string Outcome,
    long AmountMinor,
    string Currency,
    DateTime EventOccurredAt,
    string RawType) : IDomainEvent
{
    public string EventType => "payment.webhook-received";
    public string AggregateType => "payment";
    public Guid AggregateId => DeterministicEventId;
    public Guid? UserId => null;
    public object Data => new
    {
        provider = Provider,
        providerEventId = ProviderEventId,
        sessionToken = SessionToken,
        outcome = Outcome,
        amountMinor = AmountMinor,
        currency = Currency,
        occurredAt = EventOccurredAt,
        rawType = RawType,
    };
}
