namespace Shop.Domain.Enums;

/// <summary>
/// B5+/ADR-0005: the server-stated reason a Quote left the money table.
/// Expired ≠ Failed; clients must never infer it from a null transaction id.
/// Set only by the webhook/sweeper transitions that move OrderStatus.
/// </summary>
public enum PaymentOutcome
{
    Captured = 0,
    Failed = 1,
    Expired = 2,
}
