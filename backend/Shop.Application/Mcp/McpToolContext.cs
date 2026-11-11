namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: the identity every MCP tool call runs under. Anonymous
/// contexts exist only for the public READ tools; <see cref="IsAgent"/> marks a
/// delegated scope=agent token.
/// </summary>
public sealed record McpToolContext(
    Guid? UserId,
    string? Role,
    string? Scope,
    string CorrelationId,
    string? IdempotencyKey,
    string PartitionKey)
{
    public bool IsAuthenticated => UserId.HasValue;

    public bool IsAgent => string.Equals(Scope, "agent", StringComparison.OrdinalIgnoreCase);
}
