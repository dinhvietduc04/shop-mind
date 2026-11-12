using Shop.Application.Dtos;

namespace Shop.Application.Interfaces;

/// <summary>
/// M13: the advisory half of the money path. It revalidates and reprices the
/// cart exactly like <see cref="IQuoteService"/> but changes nothing — no order,
/// no payment session, no reservation. The human still reviews and pays on the
/// hosted page via <c>create_quote</c>.
/// </summary>
public interface ICheckoutPreviewService
{
    Task<CheckoutPreviewDto> PreviewAsync(Guid userId, CancellationToken ct = default);
}
