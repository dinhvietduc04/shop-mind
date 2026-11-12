namespace Shop.Application.Dtos;

/// <summary>
/// M13: a side-effect-free reprice of the cart. It answers "what would the bill
/// be right now?" — the exact subtotal, shipping and total <c>create_quote</c>
/// would produce — without creating an order, a payment session, or a stock
/// reservation. Issues are reported, not thrown, so the agent can narrate them.
/// </summary>
public sealed record CheckoutPreviewDto(
    IReadOnlyList<CheckoutPreviewLineDto> Items,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalAmount,
    bool CanQuote,
    IReadOnlyList<string> Issues);

/// <summary>One revalidated cart line (server price, never the client's).</summary>
public sealed record CheckoutPreviewLineDto(
    Guid ProductId,
    string ProductName,
    string SKU,
    decimal UnitPrice,
    int Quantity,
    int AvailableStock,
    decimal LineTotal,
    bool InStock);
