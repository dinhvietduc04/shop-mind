using Shop.Application.Common;
using Shop.Application.Dtos;

namespace Shop.Application.Interfaces;

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
