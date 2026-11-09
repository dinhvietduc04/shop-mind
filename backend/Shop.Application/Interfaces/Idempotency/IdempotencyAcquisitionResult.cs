namespace Shop.Application.Interfaces;

public enum IdempotencyAcquisitionResult
{
    Acquired,
    /// <summary>Someone else holds the lease right now → 409 + Retry-After.</summary>
    InProgress,
    /// <summary>Done, same request hash → replay the stored response.</summary>
    Replay,
    /// <summary>Done, different payload for the same key → 409 Conflict.</summary>
    PayloadMismatch,
}
