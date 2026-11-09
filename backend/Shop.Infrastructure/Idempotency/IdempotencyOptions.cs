namespace Shop.Infrastructure.Idempotency;

/// <summary>
/// B2 (ADR-0004): renewing-lease idempotency in Redis. Money paths are
/// fail-CLOSED — these knobs have no "disabled" switch by design (unlike the
/// M9 cache kill-switch, which stays fail-open).
/// </summary>
public sealed class IdempotencyOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Namespace for all idempotency keys. Production shape: "shop:idem:".</summary>
    public string KeyPrefix { get; set; } = "shop:idem:";

    /// <summary>SET-NX lease length; holder renews before it lapses.</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>How often the running request renews its lease (~lease/3).</summary>
    public int RenewSeconds { get; set; } = 10;

    /// <summary>How long a Done record replays (plan: 24h).</summary>
    public int DoneRetentionSeconds { get; set; } = 24 * 60 * 60;

    public int ConnectTimeoutMs { get; set; } = 1000;
}
