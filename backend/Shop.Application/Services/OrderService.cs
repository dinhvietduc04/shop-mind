using Microsoft.EntityFrameworkCore;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.Rules;

namespace Shop.Application.Services;

public class OrderService(IAppDbContext db) : IOrderService
{
    private IQueryable<Domain.Entities.Order> BaseQuery()
        => db.Orders.Include(o => o.Items).Include(o => o.Payment).AsQueryable();

    /// <summary>
    /// B1: a status transition is ONE conditional statement
    /// (UPDATE orders SET status=@to WHERE id=@id AND status=@from), so racing
    /// transitions serialize at the row and exactly one guard passes.
    /// 0 rows is resolved per ADR-0005's first row: if the database already holds
    /// the target status the transition is an idempotent replay; anything else is a
    /// typed InvalidOrderStateException carrying the real current status.
    /// (Mainline-downstream and PaymentAnomaly outcomes land with B5's webhook path.)
    /// Rules still come only from OrderTransitions.
    /// </summary>
    private async Task ApplyTransitionAsync(Guid orderId, OrderStatus from, OrderStatus to, CancellationToken ct)
    {
        if (from == to) return; // replay of the same state: idempotent success
        if (!OrderTransitions.IsValidTransition(from, to))
            throw new InvalidOrderStateException(orderId, from, to);

        var updated = await db.Orders
            .Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, to)
                .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);

        if (updated == 0)
        {
            var current = await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => (OrderStatus?)o.Status)
                .FirstOrDefaultAsync(ct)
                ?? throw new OrderNotFoundException(orderId);
            if (current == to) return; // raced with an identical outcome: idempotent success
            throw new InvalidOrderStateException(orderId, current, to);
        }
    }

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
        var o = await db.Orders.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Payment)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        if (o.UserId != userId) throw new UnauthorizedOrderAccessException();

        var previous = o.Status;
        if (previous == OrderStatus.Cancelled)
            return await GetMineByIdAsync(userId, orderId, ct); // idempotent replay

        if (!OrderTransitions.IsValidTransition(previous, OrderStatus.Cancelled))
            throw new InvalidOrderStateException(o.Id, previous, OrderStatus.Cancelled);

        // Status guard + inventory restore are one transaction: either the order is
        // cancelled (and its stock returned) or nothing changes.
        await using var tx = await db.BeginTransactionAsync(ct);
        await ApplyTransitionAsync(orderId, previous, OrderStatus.Cancelled, ct);

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
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);

        return await GetMineByIdAsync(userId, orderId, ct);
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
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        await ApplyTransitionAsync(orderId, o.Status, status, ct);
        return await GetAdminByIdAsync(orderId, ct);
    }
}
