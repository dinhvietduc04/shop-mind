namespace Shop.Mcp.Tooling;

/// <summary>
/// A1 contract: every tool failure is a structured, retryable-aware envelope
/// with the same correlation id as the request.
/// </summary>
public sealed record McpToolError(
    string Code,
    string Message,
    string CorrelationId,
    bool Retryable);
