using Shop.Application.Dtos;

namespace Shop.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid userId, CancellationToken ct = default);
    Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request, CancellationToken ct = default);
    Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request, CancellationToken ct = default);
    Task<CartDto> RemoveItemAsync(Guid userId, Guid itemId, CancellationToken ct = default);
    Task ClearAsync(Guid userId, CancellationToken ct = default);
}
