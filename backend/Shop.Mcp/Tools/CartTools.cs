using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Mcp.Tooling;

namespace Shop.Mcp.Tools;

/// <summary>
/// A1/A2 user-scoped cart tools. The guard requires a valid delegated JWT; the
/// user id always comes from the context, never from a tool argument. Writes
/// carry an idempotencyKey and reuse the REST validators and cart service, so
/// merge semantics and stock checks are identical to HTTP.
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
            async token => (object?)await carts.GetCartAsync(ToolArguments.RequireUserId(context), token),
            ct);
    }

    [McpServerTool(Name = "add_to_cart", Title = "Add to cart")]
    [Description("Add a product to the authenticated user's cart. Adds merge quantities; stock is checked but not reserved until a quote.")]
    public static async Task<CallToolResult> AddToCart(
        ToolExecutor executor,
        McpToolContext context,
        ICartService carts,
        IValidator<AddCartItemRequest> validator,
        [Description("The product UUID.")] Guid productId,
        [Description("Quantity to add, greater than zero.")] int quantity,
        [Description("Client-generated idempotency key, 1 to 128 characters.")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "add_to_cart",
            new { productId, quantity },
            async token =>
            {
                var request = new AddCartItemRequest(productId, quantity);
                await validator.ValidateAndThrowAsync(request, token);
                return (object?)await carts.AddItemAsync(ToolArguments.RequireUserId(context), request, token);
            },
            ct,
            idempotencyKey);
    }

    [McpServerTool(Name = "update_cart", Title = "Update cart")]
    [Description("Set the absolute quantity of one cart line owned by the authenticated user.")]
    public static async Task<CallToolResult> UpdateCart(
        ToolExecutor executor,
        McpToolContext context,
        ICartService carts,
        IValidator<UpdateCartItemRequest> validator,
        [Description("The cart item UUID.")] Guid cartItemId,
        [Description("The new absolute quantity, greater than zero.")] int quantity,
        [Description("Client-generated idempotency key, 1 to 128 characters.")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "update_cart",
            new { cartItemId, quantity },
            async token =>
            {
                var request = new UpdateCartItemRequest(quantity);
                await validator.ValidateAndThrowAsync(request, token);
                return (object?)await carts.UpdateItemAsync(
                    ToolArguments.RequireUserId(context),
                    ToolArguments.RequireId(cartItemId, "cartItemId"),
                    request,
                    token);
            },
            ct,
            idempotencyKey);
    }

    [McpServerTool(Name = "remove_from_cart", Title = "Remove from cart")]
    [Description("Remove one cart line owned by the authenticated user.")]
    public static async Task<CallToolResult> RemoveFromCart(
        ToolExecutor executor,
        McpToolContext context,
        ICartService carts,
        [Description("The cart item UUID.")] Guid cartItemId,
        [Description("Client-generated idempotency key, 1 to 128 characters.")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "remove_from_cart",
            new { cartItemId },
            async token => (object?)await carts.RemoveItemAsync(
                ToolArguments.RequireUserId(context),
                ToolArguments.RequireId(cartItemId, "cartItemId"),
                token),
            ct,
            idempotencyKey);
    }
}
