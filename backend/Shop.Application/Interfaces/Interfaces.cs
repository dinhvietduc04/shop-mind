using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Domain.Enums;

namespace Shop.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListPublicAsync(CancellationToken ct = default);
    Task<CategoryDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> ListAdminAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IProductService
{
    Task<PagedResult<ProductDto>> QueryAsync(ProductQueryParams query, CancellationToken ct = default);
    Task<ProductDetailDto> GetByIdAsync(Guid id, bool includeInactive = false, CancellationToken ct = default);
    Task<PagedResult<ProductDto>> ListAdminAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid userId, CancellationToken ct = default);
    Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request, CancellationToken ct = default);
    Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request, CancellationToken ct = default);
    Task<CartDto> RemoveItemAsync(Guid userId, Guid itemId, CancellationToken ct = default);
    Task ClearAsync(Guid userId, CancellationToken ct = default);
}

public interface ICheckoutService
{
    Task<CheckoutResponse> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<PagedResult<OrderDto>> ListMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDto> GetMineByIdAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<OrderDto> CancelMineAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<PagedResult<OrderDto>> ListAdminAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<OrderDto> GetAdminByIdAsync(Guid orderId, CancellationToken ct = default);
    Task<OrderDto> UpdateStatusAdminAsync(Guid orderId, OrderStatus status, CancellationToken ct = default);
}

public interface IInventoryService
{
    Task<PagedResult<InventoryDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<InventoryDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default);
    Task<InventoryDto> UpdateQuantityAsync(Guid productId, int quantity, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string email, string role);
}

public record FakePaymentResult(bool Succeeded, string TransactionId, string? FailureReason);

public interface IFakePaymentService
{
    Task<FakePaymentResult> ProcessAsync(Guid orderId, decimal amount, bool simulateFailure, bool simulateTimeout, CancellationToken ct = default);
}
