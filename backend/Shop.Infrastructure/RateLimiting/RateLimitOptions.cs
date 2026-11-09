namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// B3-L1: Layer-1 REST rate limits (plan §B3 table). Redis fixed windows so
/// limits survive multiple instances and are shared with the future MCP server.
/// Fail-CLOSED by design (ADR-0004): Redis down → deny, never un-throttled.
/// </summary>
public sealed class RateLimitOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Redis key namespace: shop:rl:{bucket}:{partition}.</summary>
    public string KeyPrefix { get; set; } = "shop:rl:";

    /// <summary>The "per minute" window all Layer-1 buckets use.</summary>
    public int WindowSeconds { get; set; } = 60;

    public int ConnectTimeoutMs { get; set; } = 1000;

    // Plan table, prod values. Tests override these to tiny numbers to prove
    // the contract without firing 300 requests.
    public int GlobalPerMinute { get; set; } = 300;      // per IP — the floor under every request
    public int AuthPerMinute { get; set; } = 10;         // POST /api/auth/*, per IP (credential stuffing)
    public int CatalogPerMinute { get; set; } = 120;     // catalog reads, per user-or-IP
    public int CartWritePerMinute { get; set; } = 30;    // cart writes, per user
    public int CheckoutPerMinute { get; set; } = 6;      // checkout/quote create, per user

    // The agent-chat bucket (10/min + 100/day per user) lands with A5 when
    // POST /api/agent/chat exists.
}
