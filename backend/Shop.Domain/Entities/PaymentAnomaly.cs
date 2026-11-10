namespace Shop.Domain.Entities;

/// <summary>
/// B5 (ADR-0005): append-only record of a money event that could not be applied —
/// a conflicting-terminal delivery (Cancelled/PaymentFailed receiving `succeeded`)
/// or an amount/currency mismatch. No transition happens; a human decides the
/// refund. Never updated, never deleted.
/// </summary>
public class PaymentAnomaly
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId { get; set; }
    public string? SessionToken { get; set; }
    public string ProviderEventId { get; set; } = string.Empty;

    /// <summary>What the provider asserted happened (e.g. "captured").</summary>
    public string Observed { get; set; } = string.Empty;

    /// <summary>Actual order status when the event landed.</summary>
    public string CurrentStatus { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
