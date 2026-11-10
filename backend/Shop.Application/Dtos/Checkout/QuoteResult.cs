namespace Shop.Application.Dtos;

/// <summary>POST /api/checkout/quote response shape (B5+ #2): bill + hosted page + expiry.</summary>
public sealed record QuoteResult(OrderDto Order, string HostedPayUrl, string SessionToken, DateTime ExpiresAt);
