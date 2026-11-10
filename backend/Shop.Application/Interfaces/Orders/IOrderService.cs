using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Domain.Enums;

namespace Shop.Application.Interfaces;

public interface IOrderService
{
    Task<PagedResult<OrderDto>> ListMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDto> GetMineByIdAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<OrderDto> CancelMineAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<PagedResult<OrderDto>> ListAdminAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<OrderDto> GetAdminByIdAsync(Guid orderId, CancellationToken ct = default);
    Task<OrderDto> UpdateStatusAdminAsync(Guid orderId, OrderStatus status, CancellationToken ct = default);
}
