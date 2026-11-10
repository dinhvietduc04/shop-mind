namespace Shop.Application.Interfaces;

/// <summary>
/// B5 (ADR-0001/0003): the one money path. A quote revalidates + reprices the
/// cart server-side, atomically reserves stock (B1), and attaches an expiring
/// PaymentSession. Human REST today; MCP create_quote (A2) calls the same service.
/// </summary>
public interface IQuoteService
{
    Task<Dtos.QuoteResult> CreateQuoteAsync(Guid userId, Dtos.CreateQuoteRequest request, CancellationToken ct = default);

    /// <summary>B5+ contract: the user's current Open Quote ({order, hostedPayUrl, expiresAt}) or null.</summary>
    Task<Dtos.QuoteResult?> GetOpenQuoteAsync(Guid userId, CancellationToken ct = default);
}
