namespace Shop.Domain.Events;

/// <summary>
/// B5 (ADR-0005 + plan §106): the human-attention alert emitted whenever a money
/// event was refused — conflicting terminal state or amount/currency mismatch.
/// Pairs 1:1 with an append-only PaymentAnomalies row.
/// </summary>
public sealed record PaymentAnomalyEvent(
    Guid OrderId,
    Guid? DeterministicEventId,
    string? SessionToken,
    string ProviderEventId,
    string Observed,
    string CurrentStatus,
    string Reason) : IDomainEvent
{
    public string EventType => "payment.anomaly";
    public string AggregateType => "order";
    public Guid AggregateId => OrderId;
    public Guid? UserId => null;
    public object Data => new
    {
        orderId = OrderId,
        sessionToken = SessionToken,
        providerEventId = ProviderEventId,
        observed = Observed,
        currentStatus = CurrentStatus,
        reason = Reason,
    };
}
