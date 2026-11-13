using Shop.Application.Ai;

namespace Shop.Application.Agent;

/// <summary>
/// A5: the loop sees tools only through this seam. Production implements it
/// with the official MCP client and the per-turn scope=agent token; tests can
/// substitute a deterministic gateway without changing loop logic.
/// </summary>
public interface IAgentToolGateway
{
    Task<IAgentToolSession> OpenSessionAsync(
        Guid userId,
        string role,
        string accessToken,
        string correlationId,
        CancellationToken ct = default);
}

/// <summary>One open, single-turn MCP session (token + transport + tool cache).</summary>
public interface IAgentToolSession : IAsyncDisposable
{
    IReadOnlyList<AiToolDefinition> Tools { get; }

    Task<AgentToolResult> InvokeAsync(AgentToolInvocation invocation, CancellationToken ct = default);
}

public sealed record AgentToolInvocation(
    string Name,
    string ArgumentsJson,
    string IdempotencyKey);

/// <summary>
/// Normalized tool outcome: the raw JSON the model should observe plus the
/// retry hint from the MCP error envelope. <see cref="IsError"/> never means
/// "throw" — the model gets to react to a typed tool failure.
/// </summary>
public sealed record AgentToolResult(
    bool IsError,
    string Content,
    string? ErrorCode,
    bool Retryable)
{
    public static AgentToolResult Ok(string content) => new(false, content, null, false);

    public static AgentToolResult Error(string code, string message, bool retryable) =>
        new(true, $$"""{"code":"{{code}}","message":{{System.Text.Json.JsonSerializer.Serialize(message)}},"retryable":{{(retryable ? "true" : "false")}}}""", code, retryable);
}
