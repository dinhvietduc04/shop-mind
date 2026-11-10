namespace Shop.Application.Interfaces;

/// <summary>
/// B2 (ADR-0004): idempotency record as a renewing Redis lease. A key is one of
/// absent | InProgress (held via a short lease the owner alone can renew or
/// complete) | Done (finished response, kept for replay). An abandoned lease
/// simply expires, making the key re-acquirable.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Try to become the lease holder of <paramref name="key"/> (SET NX semantics).
    /// Returns Acquired with a lease token, or reports what already exists
    /// (InProgress / Done with matching or mismatched request hash).
    /// Throws <c>IdempotencyUnavailableException</c> when the
    /// store cannot answer — money paths fail closed, never run un-deduplicated.
    /// </summary>
    Task<IdempotencyAcquisition> TryAcquireAsync(string key, string requestHash, CancellationToken ct = default);

    /// <summary>
    /// Lua compare-and-renew: extends the lease ONLY while the caller is still the
    /// recorded holder. False = not (or no longer) the holder; stop working.
    /// </summary>
    Task<bool> RenewLeaseAsync(string key, string leaseToken, CancellationToken ct = default);

    /// <summary>
    /// Owner-checked compare-and-set InProgress → Done with the response payload.
    /// Returns false when the lease was stolen or already Done — a zombie holder
    /// can never overwrite the takeover result.
    /// </summary>
    Task<bool> CompleteAsync(string key, string leaseToken, StoredIdempotentResponse response, CancellationToken ct = default);

    /// <summary>Owner-checked delete so an errored (never-completed) attempt can be retried at once.</summary>
    Task<bool> ReleaseAsync(string key, string leaseToken, CancellationToken ct = default);

    /// <summary>Current record, or null when absent / lease expired.</summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);
}
