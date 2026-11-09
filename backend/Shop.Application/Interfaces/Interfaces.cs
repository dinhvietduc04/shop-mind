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

    /// <summary>
    /// B1: atomically reserve stock for a quote with a single guarded statement
    /// (UPDATE ... WHERE quantity - reserved_quantity &gt;= q). Returns false when the
    /// guard rejects the reserve (insufficient stock); stock is then untouched.
    /// </summary>
    Task<bool> TryReserveAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// <summary>
    /// B1: payment succeeded — consume physical stock AND its reservation in one
    /// guarded statement (quantity -= q, reserved -= q WHERE both sides allow).
    /// Returns false when the guard rejects; stock is then untouched.
    /// </summary>
    Task<bool> TryCommitReservationAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// <summary>
    /// B1: expiry/failure/cancel — return reserved stock to available in one
    /// guarded statement (reserved -= q WHERE reserved &gt;= q). Returns false when
    /// the guard rejects (nothing to release); stock is then untouched.
    /// </summary>
    Task<bool> TryReleaseReservationAsync(Guid productId, int quantity, CancellationToken ct = default);
}

/// <summary>
/// B2 (ADR-0004): idempotency record as a renewing Redis lease. A key is one of
/// absent | InProgress (held via a short lease the owner alone can renew or
/// complete) | Done (finished response, kept for replay). An abandoned lease
/// simply expires, making the key re-acquirable.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Try to become the lease holder of <paramref name="key"/> (SET NX semantics).
    /// Returns Acquired with a lease token, or reports what already exists
    /// (InProgress / Done with matching or mismatched request hash).
    /// Throws <c>IdempotencyUnavailableException</c> when the
    /// store cannot answer — money paths fail closed, never run un-deduplicated.
    /// </summary>
    Task<IdempotencyAcquisition> TryAcquireAsync(string key, string requestHash, CancellationToken ct = default);

    /// <summary>
    /// Lua compare-and-renew: extends the lease ONLY while the caller is still the
    /// recorded holder. False = not (or no longer) the holder; stop working.
    /// </summary>
    Task<bool> RenewLeaseAsync(string key, string leaseToken, CancellationToken ct = default);

    /// <summary>
    /// Owner-checked compare-and-set InProgress → Done with the response payload.
    /// Returns false when the lease was stolen or already Done — a zombie holder
    /// can never overwrite the takeover result.
    /// </summary>
    Task<bool> CompleteAsync(string key, string leaseToken, StoredIdempotentResponse response, CancellationToken ct = default);

    /// <summary>Owner-checked delete so an errored (never-completed) attempt can be retried at once.</summary>
    Task<bool> ReleaseAsync(string key, string leaseToken, CancellationToken ct = default);

    /// <summary>Current record, or null when absent / lease expired.</summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);
}

public enum IdempotencyAcquisitionResult
{
    Acquired,
    /// <summary>Someone else holds the lease right now → 409 + Retry-After.</summary>
    InProgress,
    /// <summary>Done, same request hash → replay the stored response.</summary>
    Replay,
    /// <summary>Done, different payload for the same key → 409 Conflict.</summary>
    PayloadMismatch,
}

public sealed record IdempotencyAcquisition(
    IdempotencyAcquisitionResult Result,
    string? LeaseToken = null,
    int? RetryAfterSeconds = null,
    StoredIdempotentResponse? StoredResponse = null);

public enum IdempotencyState { InProgress, Done }

public sealed record IdempotencyRecord(
    string LeaseHolderToken,
    string RequestHash,
    IdempotencyState State,
    DateTimeOffset CreatedAt,
    StoredIdempotentResponse? Response = null);

public sealed record StoredIdempotentResponse(int StatusCode, string ContentType, string Body);

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
