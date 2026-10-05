using Microsoft.EntityFrameworkCore;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class OrderService(IAppDbContext db) : IOrderService
{
    private IQueryable<Domain.Entities.Order> BaseQuery()
        => db.Orders.Include(o => o.Items).Include(o => o.Payment).AsQueryable();

    public async Task<PagedResult<OrderDto>> ListMineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var q = BaseQuery().Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<OrderDto> { Items = items.Select(Mapping.ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<OrderDto> GetMineByIdAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        var o = await BaseQuery().FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        if (o.UserId != userId) throw new UnauthorizedOrderAccessException();
        return o.ToDto();
    }

    public async Task<OrderDto> CancelMineAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        var o = await db.Orders.Include(x => x.Items).Include(x => x.Payment).FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        if (o.UserId != userId) throw new UnauthorizedOrderAccessException();
        if (!o.IsCancellable)
            throw new InvalidOrderStateException(o.Id, o.Status, OrderStatus.Cancelled);

        var previous = o.Status;
        o.Cancel();

        // Restore inventory if stock was decremented (Paid -> Cancelled).
        // Pending orders in V1 never reserve stock, so only restore when previously Paid.
        if (previous == OrderStatus.Paid)
        {
            var productIds = o.Items.Select(i => i.ProductId).ToList();
            var inventories = await db.Inventories.Where(i => productIds.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, ct);
            foreach (var item in o.Items)
                if (inventories.TryGetValue(item.ProductId, out var inv))
                {
                    inv.Quantity += item.Quantity;
                    inv.UpdatedAt = DateTime.UtcNow;
                }
        }
        await db.SaveChangesAsync(ct);
        return o.ToDto();
    }

    public async Task<PagedResult<OrderDto>> ListAdminAsync(int page, int pageSize, string? status, CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var q = BaseQuery().OrderByDescending(o => o.CreatedAt).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var st))
            q = q.Where(o => o.Status == st).OrderByDescending(o => o.CreatedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<OrderDto> { Items = items.Select(Mapping.ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<OrderDto> GetAdminByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var o = await BaseQuery().FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        return o.ToDto();
    }

    public async Task<OrderDto> UpdateStatusAdminAsync(Guid orderId, OrderStatus status, CancellationToken ct = default)
    {
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        o.TransitionTo(status);
        await db.SaveChangesAsync(ct);
        return await GetAdminByIdAsync(orderId, ct);
    }
}
