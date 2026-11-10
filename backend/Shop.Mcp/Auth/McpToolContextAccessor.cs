using Microsoft.AspNetCore.Http;
using Shop.Application.Mcp;

namespace Shop.Mcp.Auth;

/// <summary>
/// A1: resolves the MCP identity from the HTTP request (delegated JWT) or, in
/// STDIO mode, from the explicit dev-principal environment variables.
/// </summary>
public sealed class McpToolContextAccessor(IHttpContextAccessor http)
{
    public McpToolContext Current
    {
        get
        {
            var context = http.HttpContext;
            if (context is not null)
            {
                return McpToolContextFactory.FromPrincipal(
                    context.User,
                    context.Request.Headers["X-Correlation-Id"].FirstOrDefault(),
                    context.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                    context.Connection.RemoteIpAddress?.ToString());
            }

            return McpToolContextFactory.FromStdio(
                Environment.GetEnvironmentVariable,
                Environment.GetEnvironmentVariable(McpToolContextFactory.DevCorrelationIdEnv),
                null);
        }
    }
}
