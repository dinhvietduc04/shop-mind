namespace Shop.Domain.Enums;

/// <summary>
/// A6 Assist lifecycle. <see cref="Pending"/> exists only for the future
/// Delegated confirm-to-execute seam; every Assist quote is decided in the
/// turn that created it (Executed or Rejected) and may later expire unpaid.
/// </summary>
public enum AiActionStatus
{
    Pending = 0,
    Executed = 1,
    Rejected = 2,
    Expired = 3,
}
