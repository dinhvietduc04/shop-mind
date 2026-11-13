namespace Shop.Application.Interfaces;

/// <summary>
/// ADR-0006/A5: the AgentLoop is the ONLY component allowed to mint a
/// delegated scope=agent token. The token is audience Shop.Mcp, lives for one
/// turn (hard cap 60s), is handed only to the MCP client, and is never
/// persisted, serialized to a client, or logged.
/// </summary>
public interface IAgentTokenMinter
{
    string Mint(Guid userId, string role, TimeSpan requestedTtl);
}
