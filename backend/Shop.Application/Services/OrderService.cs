using Microsoft.EntityFrameworkCore;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.Rules;

namespace Shop.Application.Services;

public class OrderService(IAppDbContext db, IEventPublisher events, OrderStatusTransitioner transitioner, IInventoryService inventory) : IOrderService
{
    private IQueryable<Domain.Entities.Order> BaseQuery()
        => db.Orders.Include(o => o.Items).Include(o => o.Payment).AsQueryable();

    private enum TransitionOutcome { Transitioned, Replayed }

    /// <summary>
    /// B1: conditional-update transition via the shared OrderStatusTransitioner
    /// (single source of truth for the ADR-0005 replay resolution).
    /// </summary>
    private async Task<TransitionOutcome> ApplyTransitionAsync(Guid orderId, OrderStatus from, OrderStatus to, CancellationToken ct)
        => await transitioner.ApplyAsync(orderId, from, to, ct) == OrderStatusTransitioner.Outcome.Transitioned
            ? TransitionOutcome.Transitioned
            : TransitionOutcome.Replayed;

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
        var outcome = await ApplyTransitionAsync(orderId, previous, OrderStatus.Cancelled, ct);

        // M10/B1 fix: a REPLAYED cancel (concurrent cancel already won) must have no
        // side effects — otherwise the loser would restore stock a second time and
        // re-fire the event. Business work happens only on a real transition.
        if (outcome == TransitionOutcome.Transitioned)
        {
            // B5: quote holds release through the guarded primitives (physical stock
            // was never decremented); legacy Paid orders restore quantity (M5 shape).
            var holds = o.Items.Where(i => i.ReservedQuantity > 0).ToList();
            if (previous == OrderStatus.Paid && holds.Count == 0)
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
            foreach (var item in holds)
                await inventory.TryReleaseReservationAsync(item.ProductId, item.ReservedQuantity, ct);
            if (holds.Count > 0)
                await db.OrderItems.Where(i => i.OrderId == orderId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReservedQuantity, 0), ct);

            // order.cancelled enqueued inside the SAME transaction as the status
            // guard + stock release; SaveChanges flushes outbox and business rows together.
            await events.EnqueueAsync(new Shop.Domain.Events.OrderCancelledEvent(orderId, o.UserId), ct);
        }
        await db.SaveChangesAsync(ct);
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

        // M10: transaction spans the conditional transition + its catalog event,
        // so a committed status change always has its outbox row (and vice versa).
        await using var tx = await db.BeginTransactionAsync(ct);
        var outcome = await ApplyTransitionAsync(orderId, o.Status, status, ct);
        if (outcome == TransitionOutcome.Transitioned)
        {
            Shop.Domain.Events.IDomainEvent? domainEvent = status switch
            {
                OrderStatus.Paid => await PaidEventAsync(orderId, o, ct),
                OrderStatus.Shipped => new Shop.Domain.Events.OrderShippedEvent(orderId, o.UserId),
                OrderStatus.Delivered => new Shop.Domain.Events.OrderDeliveredEvent(orderId, o.UserId),
                OrderStatus.Cancelled => new Shop.Domain.Events.OrderCancelledEvent(orderId, o.UserId, "admin"),
                _ => null, // Processing / PaymentFailed-by-admin: no catalog event (payment.failed arrives with B5's webhook flow)
            };
            if (domainEvent is not null)
                await events.EnqueueAsync(domainEvent, ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAdminByIdAsync(orderId, ct);
    }

    private async Task<Shop.Domain.Events.OrderPaidEvent> PaidEventAsync(
        Guid orderId, Domain.Entities.Order order, CancellationToken ct)
    {
        var transactionId = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .Select(p => p.TransactionId)
            .FirstOrDefaultAsync(ct);
        return new Shop.Domain.Events.OrderPaidEvent(orderId, order.UserId, order.TotalAmount, transactionId);
    }
}
