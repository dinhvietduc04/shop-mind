using System.Security.Claims;

namespace Shop.Api.Extensions;

/// <summary>ADR-0006: agent-scope JWTs (delegated tokens from the MCP layer) carry scope=agent.</summary>
public static class AgentScopeExtensions
{
    public static bool IsAgentToken(this ClaimsPrincipal user) =>
        user.FindFirstValue("scope") is "agent";
}
