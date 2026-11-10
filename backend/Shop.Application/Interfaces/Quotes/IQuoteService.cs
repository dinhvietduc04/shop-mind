using Shop.Domain.Enums;

namespace Shop.Application.Interfaces;

/// <summary>
/// B5 (ADR-0001/0003): the one money path. A quote revalidates + reprices the
/// cart server-side, atomically reserves stock (B1), and attaches an expiring
/// PaymentSession. REST passes Human, MCP create_quote (A2) passes Agent; the
/// origin scopes the replace policy (an agent may never cancel a human quote).
/// </summary>
public interface IQuoteService
{
    Task<Dtos.QuoteResult> CreateQuoteAsync(Guid userId, Dtos.CreateQuoteRequest request, QuoteOrigin origin, CancellationToken ct = default);

    /// <summary>B5+ contract: the user's current Open Quote ({order, hostedPayUrl, expiresAt}) or null.</summary>
    Task<Dtos.QuoteResult?> GetOpenQuoteAsync(Guid userId, CancellationToken ct = default);
}
