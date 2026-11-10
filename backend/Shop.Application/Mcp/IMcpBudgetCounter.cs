namespace Shop.Application.Mcp;

/// <summary>A1: one server-side MCP budget decision for a (partition, risk class) pair.</summary>
public sealed record McpBudgetDecision(
    bool Allowed,
    int Limit,
    int Remaining,
    TimeSpan RetryAfter);

/// <summary>
/// A1 / B3-L2: MCP-side budgets are independent of the REST layer. The counter
/// is fail-closed: if Redis cannot answer, the call is denied, never unthrottled.
/// </summary>
public interface IMcpBudgetCounter
{
    Task<McpBudgetDecision> TryConsumeAsync(
        string partition,
        ToolRiskClass riskClass,
        CancellationToken ct = default);
}
