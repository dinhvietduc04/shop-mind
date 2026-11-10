using Shop.Domain.Enums;

namespace Shop.Application.Events;

/// <summary>
/// B5: the normalized provider webhook contract (grilled decision: the Stripe
/// swap at milestone 13 = a new parser feeding this same shape, zero consumer
/// change). ProviderEventId is the dedupe key (→ ProcessedEvents, consumer
/// "payment-webhook"); SessionToken resolves the PaymentSession.
/// </summary>
public sealed record PaymentProviderEvent(
    string Provider,
    string ProviderEventId,
    string SessionToken,
    PaymentOutcome Outcome,
    long AmountMinor,
    string Currency,
    DateTime OccurredAt,
    string RawType);
