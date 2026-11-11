namespace Shop.Application.Mcp;

/// <summary>
/// A1 / B3-L2: MCP budget + payload limits. The risk-class limits mirror the
/// plan table; write/quote values are present now so A2 can reuse the counter.
/// </summary>
public sealed class McpOptions
{
    /// <summary>Audience minted into delegated agent JWTs.</summary>
    public string Audience { get; set; } = "Shop.Mcp";

    /// <summary>Redis namespace for the sliding-window MCP budgets.</summary>
    public string KeyPrefix { get; set; } = "shop:mcp:rl:";

    public int ReadPerMin { get; set; } = 30;
    public int WritePerMin { get; set; } = 12;
    public int QuotePerMin { get; set; } = 4;
    public int WindowSeconds { get; set; } = 60;

    public int MaxArgsBytes { get; set; } = 4096;
    public int MaxResultBytes { get; set; } = 32768;
    public int MaxPageSize { get; set; } = 50;
}
