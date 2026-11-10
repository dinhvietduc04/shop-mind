using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Events;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Events;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

/// <summary>
/// B5: the single applier of payment fates (ADR-0005 matrix over B1 guarded
/// primitives). One transaction: dedupe mark + fate writes commit together;
/// any fault rolls back so a redelivery reprocesses from scratch. Amount and
/// currency are verified against the PaymentSession BEFORE anything moves —
/// a mismatch is an anomaly (human decides), never a guessed amount.
/// </summary>
public sealed class PaymentWebhookConsumer(
    IAppDbContext db,
    IInventoryService inventory,
    OrderStatusTransitioner transitioner,
    IEventPublisher events,
    QuoteExpirySweeper sweeper)
{
    public const string DedupeConsumer = "payment-webhook";

    public async Task<WebhookApplyOutcome> ApplyAsync(PaymentProviderEvent evt, CancellationToken ct = default)
    {
        await using var tx = await db.BeginTransactionAsync(ct);

        // 1 — dedupe (unique (EventId, Consumer) is the arbiter for races).
        var intakeId = ProviderEventIds.ToGuid(evt.ProviderEventId);
        try
        {
            if (await db.ProcessedEvents.AsNoTracking()
                    .AnyAsync(p => p.EventId == intakeId && p.Consumer == DedupeConsumer, ct))
            {
                await tx.RollbackAsync(ct);
                return WebhookApplyOutcome.Duplicate;
            }
            db.ProcessedEvents.Add(new ProcessedEvent { EventId = intakeId, Consumer = DedupeConsumer });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            db.ClearChangeTracker();
            return WebhookApplyOutcome.Duplicate;
        }

        // 2 — resolve the session; junk tokens burn their dedupe slot (no retry loop).
        var session = await db.PaymentSessions.AsNoTracking()
            .Include(s => s.Order!).ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(s => s.SessionToken == evt.SessionToken, ct);
        if (session?.Order is null)
        {
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.NoSession;
        }

        var order = session.Order;

        // 3 — money verification vs the session, before any state moves.
        var expectedMinor = (long)decimal.Round(session.Amount * 100m, MidpointRounding.AwayFromZero);
        if (evt.AmountMinor != expectedMinor || !string.Equals(evt.Currency, session.Currency, StringComparison.OrdinalIgnoreCase))
        {
            await RecordAnomalyAsync(order, session, evt, intakeId,
                $"amount/currency mismatch: provider said {evt.AmountMinor} {evt.Currency}, session is {expectedMinor} {session.Currency}", ct);
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.Anomaly;
        }

        switch (evt.Outcome)
        {
            case PaymentOutcome.Captured:
                return await ApplyCapturedAsync(order, session, evt, intakeId, tx, ct);

            case PaymentOutcome.Failed:
                return await ApplyFailedAsync(order, evt, intakeId, tx, ct);

            case PaymentOutcome.Expired:
                // Identical core to the sweeper — running inside THIS transaction,
                // so expiry + dedupe commit atomically and racing the sweeper is safe.
                await sweeper.ApplyExpiredCoreAsync(order.Id, ct);
                await tx.CommitAsync(ct);
                return WebhookApplyOutcome.Applied;

            default:
                await tx.CommitAsync(ct);
                return WebhookApplyOutcome.Applied;
        }
    }

    private async Task<WebhookApplyOutcome> ApplyCapturedAsync(
        Order order, PaymentSession session, PaymentProviderEvent evt, Guid intakeId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        // ADR-0005: conflicting terminal side-branches receiving `Paid` = anomaly.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PaymentFailed)
        {
            await RecordAnomalyAsync(order, session, evt, intakeId,
                $"late 'succeeded' landed on a {order.Status} order", ct);
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.Anomaly;
        }

        if (order.Status == OrderStatus.Paid)
        {
            // ADR-0005 row 1: already at target — idempotent success, no rows.
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.Applied;
        }

        var transitioned = await transitioner.ApplyAsync(order.Id, OrderStatus.Pending, OrderStatus.Paid, ct);
        if (transitioned == OrderStatusTransitioner.Outcome.Replayed)
        {
            await tx.CommitAsync(ct); // a racing writer already applied the same fate
            return WebhookApplyOutcome.Applied;
        }

        try
        {
            // Commit every line's hold with B1 guards — a vanishing reservation
            // throws so the whole transaction rolls back (no half-paid state).
            foreach (var item in order.Items)
            {
                var held = item.ReservedQuantity > 0 ? item.ReservedQuantity : item.Quantity;
                if (!await inventory.TryCommitReservationAsync(item.ProductId, held, ct))
                    throw new InvalidOperationException($"reservation for product {item.ProductId} vanished at commit");
            }

            db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Amount = session.Amount,
                Status = PaymentStatus.Succeeded,
                TransactionId = evt.ProviderEventId,
            });
            await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(
                s => s.SetProperty(o => o.Outcome, PaymentOutcome.Captured), ct);
            await db.OrderItems.Where(i => i.OrderId == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReservedQuantity, 0), ct);

            // Clear ONLY the quoted lines (the human may have added other items since).
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var cart = await db.Carts.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == order.UserId, ct);
            if (cart is not null)
                await db.CartItems.Where(i => i.CartId == cart.Id && productIds.Contains(i.ProductId))
                    .ExecuteDeleteAsync(ct);

            await events.EnqueueAsync(
                new OrderPaidEvent(order.Id, order.UserId, order.TotalAmount, evt.ProviderEventId), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.Applied;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Commit guards failed mid-application: the tx above rolls back
            // everything; record the anomaly on a fresh transaction for humans.
            await tx.RollbackAsync(ct);
            db.ClearChangeTracker();
            await using var anomalyTx = await db.BeginTransactionAsync(ct);
            await RecordAnomalyAsync(order, session, evt, intakeId, ex.Message, ct);
            await anomalyTx.CommitAsync(ct);
            return WebhookApplyOutcome.Anomaly;
        }
    }

    private async Task<WebhookApplyOutcome> ApplyFailedAsync(
        Order order, PaymentProviderEvent evt, Guid intakeId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        if (order.Status == OrderStatus.Paid)
        {
            // failed after paid → refund decision, not our state machine.
            await RecordAnomalyAsync(order, null, evt, intakeId, "'failed' landed on a Paid order", ct);
            await tx.CommitAsync(ct);
            return WebhookApplyOutcome.Anomaly;
        }
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PaymentFailed)
        {
            await tx.CommitAsync(ct); // harmless replay — already out of the money game
            return WebhookApplyOutcome.Applied;
        }

        var transitioned = await transitioner.ApplyAsync(order.Id, OrderStatus.Pending, OrderStatus.PaymentFailed, ct);
        if (transitioned == OrderStatusTransitioner.Outcome.Transitioned)
        {
            foreach (var item in order.Items.Where(i => i.ReservedQuantity > 0))
                await inventory.TryReleaseReservationAsync(item.ProductId, item.ReservedQuantity, ct);

            db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Status = PaymentStatus.Failed,
                TransactionId = evt.ProviderEventId,
            });
            await db.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Outcome, PaymentOutcome.Failed), ct);
            await db.OrderItems.Where(i => i.OrderId == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReservedQuantity, 0), ct);
            await events.EnqueueAsync(new PaymentFailedEvent(order.Id, order.UserId, evt.RawType), ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return WebhookApplyOutcome.Applied;
    }

    private async Task RecordAnomalyAsync(
        Order order, PaymentSession? session, PaymentProviderEvent evt, Guid intakeId, string reason, CancellationToken ct)
    {
        db.PaymentAnomalies.Add(new PaymentAnomaly
        {
            OrderId = order.Id,
            SessionToken = session?.SessionToken ?? evt.SessionToken,
            ProviderEventId = evt.ProviderEventId,
            Observed = evt.Outcome.ToString(),
            CurrentStatus = order.Status.ToString(),
            Reason = reason,
        });
        await events.EnqueueAsync(new PaymentAnomalyEvent(
            order.Id, intakeId, evt.SessionToken, evt.ProviderEventId,
            evt.Outcome.ToString(), order.Status.ToString(), reason), ct);
        await db.SaveChangesAsync(ct);
    }
}
