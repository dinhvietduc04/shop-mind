using Microsoft.EntityFrameworkCore;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Events;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

/// <summary>
/// B5 (plan §B4/B5): expires Open Quotes whose hold elapsed — conditional
/// Pending→Cancelled (racing a late webhook: first transition guard wins, the
/// loser no-ops — never a double release), guarded release of each line's
/// reservation, server-stated outcome=expired, order.cancelled event. Legacy V1
/// Pendings are excluded by construction (ExpiresAt IS NULL — ADR-0001).
/// </summary>
public sealed class QuoteExpirySweeper(
    IAppDbContext db,
    IInventoryService inventory,
    OrderStatusTransitioner transitioner,
    IEventPublisher events)
{
    public async Task<int> SweepAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var expiredIds = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.ExpiresAt != null && o.ExpiresAt < nowUtc)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var swept = 0;
        foreach (var id in expiredIds)
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                if (await ApplyExpiredCoreAsync(id, ct))
                {
                    swept++;
                    await tx.CommitAsync(ct);
                }
                else
                {
                    await tx.RollbackAsync(ct);
                }
            }
            catch (InvalidOrderStateException)
            {
                // A late webhook captured/failed it first — that fate stands.
                await tx.RollbackAsync(ct);
                db.ClearChangeTracker();
            }
        }
        return swept;
    }

    /// <summary>
    /// Per-order core, joining the CALLER's ambient transaction (the webhook
    /// consumer embeds it so expiry + dedupe commit atomically). Returns false
    /// when this order was not ours to expire (already decided elsewhere).
    /// </summary>
    public async Task<bool> ApplyExpiredCoreAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == OrderStatus.Pending, ct)
            ?? throw new OrderNotFoundException(orderId);

        var transitioned = await transitioner.ApplyAsync(orderId, OrderStatus.Pending, OrderStatus.Cancelled, ct);
        if (transitioned == OrderStatusTransitioner.Outcome.Replayed)
            return false;

        foreach (var item in order.Items.Where(i => i.ReservedQuantity > 0))
            await inventory.TryReleaseReservationAsync(item.ProductId, item.ReservedQuantity, ct);

        await db.Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Outcome, PaymentOutcome.Expired), ct);
        await db.OrderItems.Where(i => i.OrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReservedQuantity, 0), ct);
        await events.EnqueueAsync(new OrderCancelledEvent(orderId, order.UserId, "expired"), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
