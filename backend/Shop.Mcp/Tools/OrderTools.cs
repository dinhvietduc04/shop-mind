using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Mcp.Tooling;

namespace Shop.Mcp.Tools;

/// <summary>
/// A2 owner-scoped order reads. Ownership is enforced inside IOrderService; the
/// user id always comes from the delegated token, never from a tool argument.
/// </summary>
[McpServerToolType]
public static class OrderTools
{
    [McpServerTool(Name = "get_orders", Title = "Get orders")]
    [Description("List the authenticated user's orders and quotes, newest first.")]
    public static async Task<CallToolResult> GetOrders(
        ToolExecutor executor,
        McpToolContext context,
        IOrderService orders,
        [Description("One-based page number.")] int page = 1,
        [Description("Page size, 1 through 50.")] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_orders",
            new { page, pageSize },
            async token =>
            {
                ToolArguments.ValidatePage(page, pageSize, 50);
                var userId = ToolArguments.RequireUserId(context);
                return (object?)await orders.ListMineAsync(userId, page, pageSize, token);
            },
            ct);
    }

    [McpServerTool(Name = "get_order", Title = "Get order")]
    [Description("Get one of the authenticated user's orders by its UUID.")]
    public static async Task<CallToolResult> GetOrder(
        ToolExecutor executor,
        McpToolContext context,
        IOrderService orders,
        [Description("The order UUID.")] Guid orderId,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_order",
            new { orderId },
            async token =>
            {
                var userId = ToolArguments.RequireUserId(context);
                return (object?)await orders.GetMineByIdAsync(userId, ToolArguments.RequireId(orderId, "orderId"), token);
            },
            ct);
    }

    [McpServerTool(Name = "get_order_status", Title = "Get order status")]
    [Description("Get just the status projection of one of the authenticated user's orders.")]
    public static async Task<CallToolResult> GetOrderStatus(
        ToolExecutor executor,
        McpToolContext context,
        IOrderService orders,
        [Description("The order UUID.")] Guid orderId,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_order_status",
            new { orderId },
            async token =>
            {
                var userId = ToolArguments.RequireUserId(context);
                var order = await orders.GetMineByIdAsync(
                    userId, ToolArguments.RequireId(orderId, "orderId"), token);

                return (object?)new OrderStatusView(
                    order.Id, order.OrderNumber, order.Status, order.TotalAmount, order.CreatedAt);
            },
            ct);
    }

    private sealed record OrderStatusView(
        Guid Id,
        string OrderNumber,
        string Status,
        decimal TotalAmount,
        DateTime CreatedAt);
}
