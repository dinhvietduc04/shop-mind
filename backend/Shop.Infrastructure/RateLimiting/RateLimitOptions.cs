using Microsoft.Extensions.Options;

namespace Shop.Infrastructure.RateLimiting;

/// <summary>
/// B3-L1: Layer-1 REST rate limits (plan §B3 table). Redis fixed windows so
/// limits survive multiple instances and are shared with the future MCP server.
/// Fail-CLOSED by design (ADR-0004): Redis down → deny, never un-throttled.
/// Bound via <see cref="IOptionsMonitor{T}"/>, so edits to the <c>RateLimit</c>
/// configuration section apply to the next request without a restart.
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
    // A limit of zero or less disables that bucket (no counter, no headers).

    /// <summary>Per IP — the floor under every request. &lt;= 0 disables it.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>POST /api/auth/*, per IP (credential stuffing). &lt;= 0 disables it.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Catalog reads, per user-or-IP. &lt;= 0 disables it.</summary>
    public int CatalogPerMinute { get; set; } = 120;

    /// <summary>Cart writes, per user. &lt;= 0 disables it.</summary>
    public int CartWritePerMinute { get; set; } = 30;

    /// <summary>Checkout/quote create, per user. &lt;= 0 disables it.</summary>
    public int CheckoutPerMinute { get; set; } = 6;

    // The agent-chat bucket (10/min + 100/day per user) lands with A5 when
    // POST /api/agent/chat exists.
}
