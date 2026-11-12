using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Domain.Enums;
using Shop.Mcp.Tooling;

namespace Shop.Mcp.Tools;

/// <summary>
/// A2: the agent may create a quote for the human to review and pay, never pay
/// it. The quote is tagged Agent origin, so it may replace only another
/// agent-origin open quote; a human bill blocks it with QUOTE_HELD_BY_HUMAN.
/// M13 adds the read-only <c>prepare_checkout</c> dry run, which creates
/// nothing — no order, no session, no reservation.
/// </summary>
[McpServerToolType]
public static class QuoteTools
{
    [McpServerTool(Name = "prepare_checkout", Title = "Prepare checkout")]
    [Description("Dry-run the bill for the authenticated user's cart: revalidates stock and returns the exact subtotal, shipping, total and per-line issues that create_quote would produce. Creates nothing and reserves no stock.")]
    public static async Task<CallToolResult> PrepareCheckout(
        ToolExecutor executor,
        McpToolContext context,
        ICheckoutPreviewService preview,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "prepare_checkout",
            null,
            async token => (object?)await preview.PreviewAsync(
                ToolArguments.RequireUserId(context), token),
            ct);
    }

    [McpServerTool(Name = "create_quote", Title = "Create quote")]
    [Description("Create a bill from the authenticated user's cart for the human to review and pay. The agent never confirms payment.")]
    public static async Task<CallToolResult> CreateQuote(
        ToolExecutor executor,
        McpToolContext context,
        IQuoteService quotes,
        IValidator<CreateQuoteRequest> validator,
        [Description("Recipient full name.")] string? fullName = null,
        [Description("Recipient phone number.")] string? phone = null,
        [Description("Street address.")] string? address = null,
        [Description("City.")] string? city = null,
        [Description("Client-generated idempotency key, 1 to 128 characters.")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "create_quote",
            new { fullName, phone, address, city },
            async token =>
            {
                var request = new CreateQuoteRequest(
                    new ShippingAddressDto(fullName!, phone!, address!, city!));
                await validator.ValidateAndThrowAsync(request, token);
                return (object?)await quotes.CreateQuoteAsync(
                    ToolArguments.RequireUserId(context),
                    request,
                    QuoteOrigin.Agent,
                    token);
            },
            ct,
            idempotencyKey);
    }
}
