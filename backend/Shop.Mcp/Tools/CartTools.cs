using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Mcp.Tooling;

namespace Shop.Mcp.Tools;

/// <summary>
/// A1 user-scoped cart read. The guard requires a valid delegated JWT; the
/// user id always comes from the context, never from a tool argument.
/// </summary>
[McpServerToolType]
public static class CartTools
{
    [McpServerTool(Name = "get_cart", Title = "Get cart")]
    [Description("Get the authenticated user's cart.")]
    public static async Task<CallToolResult> GetCart(
        ToolExecutor executor,
        McpToolContext context,
        ICartService carts,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_cart",
            null,
            async token =>
            {
                var userId = context.UserId
                    ?? throw new UnauthorizedAccessException("A valid user token is required for this tool.");
                return (object?)await carts.GetCartAsync(userId, token);
            },
            ct);
    }
}
