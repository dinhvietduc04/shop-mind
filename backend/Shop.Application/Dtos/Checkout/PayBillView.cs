namespace Shop.Application.Dtos;

/// <summary>
/// B5 (ADR-0002): the pay-page capability view. Resolved from the session token
/// alone — this read is the invoice view an agent may read but never act on.
/// </summary>
public sealed record PayBillView(
    Guid SessionId,
    string Provider,
    string OrderNumber,
    decimal Subtotal,
    decimal Shipping,
    decimal Total,
    string Currency,
    string OrderStatus,
    PaymentOutcomeDto? Outcome,
    DateTime ExpiresAt,
    IReadOnlyList<OrderItemDto> Items);

/// <summary>Wire names of Shop.Domain.Enums.PaymentOutcome (B5+: captured|failed|expired).</summary>
public enum PaymentOutcomeDto { captured, failed, expired }

/// <summary>Session coordinates confirmation needs (amount + owner + provider).</summary>
public sealed record PaySessionView(Guid Id, Guid OrderId, Guid UserId, string SessionToken, decimal Amount, string Currency, string Provider);
