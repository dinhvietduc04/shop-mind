using Microsoft.EntityFrameworkCore;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.Rules;

namespace Shop.Application.Services;

/// <summary>
/// B1/B5: the single implementation of conditional status transitions —
/// one UPDATE guarded on the expected status (racing writers serialize at the
/// row), with the ADR-0005 outcome matrix for "0 rows affected":
///   equal to target          → replay (idempotent success, fire no events)
///   anything else            → typed InvalidOrderStateException with real status
/// (PaymentAnomaly detection lives in the webhook consumer, which owns the
/// conflicting-terminal context.) Rules come only from OrderTransitions.
/// </summary>
public sealed class OrderStatusTransitioner(IAppDbContext db)
{
    public enum Outcome { Transitioned, Replayed }

    public async Task<Outcome> ApplyAsync(Guid orderId, OrderStatus from, OrderStatus to, CancellationToken ct = default)
    {
        if (from == to) return Outcome.Replayed;
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
            if (current == to) return Outcome.Replayed;
            throw new InvalidOrderStateException(orderId, current, to);
        }
        return Outcome.Transitioned;
    }
}
