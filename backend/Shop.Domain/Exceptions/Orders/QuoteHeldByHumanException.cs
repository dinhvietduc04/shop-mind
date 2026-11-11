namespace Shop.Domain.Exceptions;

/// <summary>
/// A2 / B3 quote budget row: an agent tried to replace an Open Quote a human
/// created. Typed so both REST and MCP surface the same QUOTE_HELD_BY_HUMAN
/// code; the agent narrates "pay or cancel your existing bill first".
/// </summary>
public class QuoteHeldByHumanException : ShopDomainException
{
    public Guid OrderId { get; }

    public QuoteHeldByHumanException(Guid orderId)
        : base("QUOTE_HELD_BY_HUMAN",
            $"A quote created by a human is open for order '{orderId}'. The agent cannot replace it; the human must pay or cancel it first.")
    {
        OrderId = orderId;
    }
}
