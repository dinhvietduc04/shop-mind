using Shop.Application.Dtos;

namespace Shop.Application.Interfaces;

public interface ICheckoutService
{
    Task<CheckoutResponse> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default);
}
