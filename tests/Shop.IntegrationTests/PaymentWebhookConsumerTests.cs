using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Events;
using Shop.Application.Services;
using Shop.Domain;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B5 (ADR-0005): the single money-fate applier — captured/failed/expired fates,
/// the ADR-0005 conflict rows (anomaly, never a silent transition), amount
/// verification against the session, provider-event dedupe, and replay-onto-
/// target = silent success. Seam: PaymentWebhookConsumer.ApplyAsync over the
/// real quote/stock/cart state from QuoteHarness.
/// </summary>
public class PaymentWebhookConsumerTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private readonly QuoteHarness _h = new(factory);
    private PaymentWebhookConsumer Consumer() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<PaymentWebhookConsumer>();

    private static PaymentProviderEvent Evt(QuoteHarness.Opened o, PaymentOutcome outcome, long? amountMinor = null, string? token = null)
        => new("fake-psp", $"evt-{Guid.NewGuid():N}", token ?? o.Quote.SessionToken, outcome,
            amountMinor ?? (long)(o.Total * 100m), "USD", DateTime.UtcNow, "test");

    [Fact]
    public async Task Captured_AppliesTheFullFate()
    {
        var o = await _h.OpenQuoteAsync(stock: 10, price: 50m, qty: 1); // 50 + 15 ship = 65
        var evt = Evt(o, PaymentOutcome.Captured);

        var outcome = await Consumer().ApplyAsync(evt);

        Assert.Equal(WebhookApplyOutcome.Applied, outcome);
        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentOutcome.Captured, order.Outcome);
        Assert.Equal(PaymentStatus.Succeeded, order.Payment?.Status);

        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(9, stock.Quantity);   // committed
        Assert.Equal(0, stock.ReservedQuantity);

        var cart = await _h.CartAsync(o.UserId);
        Assert.Empty(cart!.Items); // quoted lines cleared, cart object survives

        var db = _h.NewDb();
        Assert.Equal(1, await db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "order.paid" && m.AggregateId == o.Quote.Order.Id));
        Assert.Equal(1, await db.ProcessedEvents.AsNoTracking()
            .CountAsync(p => p.EventId == Shop.Domain.ProviderEventIds.ToGuid(evt.ProviderEventId) && p.Consumer == "payment-webhook"));
    }

    [Fact]
    public async Task TripleDelivery_AppliedOnce()
    {
        // B2/B5 exit criterion: the same provider event 3× → ONE transition.
        var o = await _h.OpenQuoteAsync();
        var evt = Evt(o, PaymentOutcome.Captured);

        var first = await Consumer().ApplyAsync(evt);
        var second = await Consumer().ApplyAsync(evt);
        var third = await Consumer().ApplyAsync(evt);

        Assert.Equal(WebhookApplyOutcome.Applied, first);
        Assert.Equal(WebhookApplyOutcome.Duplicate, second);
        Assert.Equal(WebhookApplyOutcome.Duplicate, third);
        Assert.Equal(1, await _h.NewDb().OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "order.paid" && m.AggregateId == o.Quote.Order.Id));
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(0, stock.ReservedQuantity); // committed exactly once
    }

    [Fact]
    public async Task AmountMismatch_AnomalyWithoutAnyTransition()
    {
        var o = await _h.OpenQuoteAsync();
        var evt = Evt(o, PaymentOutcome.Captured, amountMinor: 100); // provider paid cents, not our total

        var outcome = await Consumer().ApplyAsync(evt);

        Assert.Equal(WebhookApplyOutcome.Anomaly, outcome);
        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.Outcome);
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(1, stock.ReservedQuantity); // untouched
        Assert.Equal(1, await _h.NewDb().PaymentAnomalies.AsNoTracking().CountAsync(a => a.OrderId == o.Quote.Order.Id));
        Assert.Equal(1, await _h.NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "payment.anomaly" && m.AggregateId == o.Quote.Order.Id));
    }

    [Fact]
    public async Task Failed_ReleasesReservationsAndRecordsFailedPayment()
    {
        var o = await _h.OpenQuoteAsync();

        Assert.Equal(WebhookApplyOutcome.Applied, await Consumer().ApplyAsync(Evt(o, PaymentOutcome.Failed)));

        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal(PaymentOutcome.Failed, order.Outcome);
        Assert.Equal(PaymentStatus.Failed, order.Payment?.Status);
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(10, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity); // released
        Assert.Equal(1, await _h.NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "payment.failed"));
    }

    [Fact]
    public async Task Expired_MatchesTheSweeperPath()
    {
        var o = await _h.OpenQuoteAsync();

        Assert.Equal(WebhookApplyOutcome.Applied, await Consumer().ApplyAsync(Evt(o, PaymentOutcome.Expired)));

        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentOutcome.Expired, order.Outcome);
        Assert.Equal(0, (await _h.StockAsync(o.ProductId)).ReservedQuantity);
    }

    [Fact]
    public async Task CapturedIntoCancelled_AnomalyNoTransition_StockStaysReleased()
    {
        // ADR-0005 conflicting-terminal row: the human cancelled (or replaced) the
        // quote, then a late "succeeded" lands. No transition, anomaly + alert;
        // and cancel must already have released the reservation (quote-aware fix).
        var o = await _h.OpenQuoteAsync();
        await factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<Shop.Application.Interfaces.IOrderService>()
            .CancelMineAsync(o.UserId, o.Quote.Order.Id);

        var afterCancel = await _h.StockAsync(o.ProductId);
        Assert.Equal(0, afterCancel.ReservedQuantity); // cancel released the hold

        var outcome = await Consumer().ApplyAsync(Evt(o, PaymentOutcome.Captured));

        Assert.Equal(WebhookApplyOutcome.Anomaly, outcome);
        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(10, stock.Quantity); // no commit, no double effect
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(1, await _h.NewDb().PaymentAnomalies.AsNoTracking().CountAsync(a => a.OrderId == o.Quote.Order.Id));
        Assert.Equal(1, await _h.NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "payment.anomaly" && m.AggregateId == o.Quote.Order.Id));
    }

    [Fact]
    public async Task CapturedWhenAlreadyPaid_IsSilentReplay_NoSecondPayment()
    {
        // ADR-0005 row 1 with a DIFFERENT provider event id (double-charge attempt
        // by a confused PSP): replay onto target = idempotent success, no new rows.
        var o = await _h.OpenQuoteAsync();
        await Consumer().ApplyAsync(Evt(o, PaymentOutcome.Captured));

        var again = await Consumer().ApplyAsync(Evt(o, PaymentOutcome.Captured)); // new provider event id

        Assert.Equal(WebhookApplyOutcome.Applied, again);
        Assert.Equal(1, await _h.NewDb().Payments.AsNoTracking().CountAsync(p => p.OrderId == o.Quote.Order.Id));
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(0, stock.ReservedQuantity); // not committed twice (stock already consumed by the first)
        Assert.Equal(1, await _h.NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "order.paid" && m.AggregateId == o.Quote.Order.Id));
    }

    [Fact]
    public async Task UnknownSession_NoTransition_ButDeduped()
    {
        var o = await _h.OpenQuoteAsync();
        var evt = Evt(o, PaymentOutcome.Captured, token: "not-a-real-session-token");

        Assert.Equal(WebhookApplyOutcome.NoSession, await Consumer().ApplyAsync(evt));
        Assert.Equal(OrderStatus.Pending, (await _h.OrderAsync(o.Quote.Order.Id)).Status);
        Assert.Equal(1, await _h.NewDb().ProcessedEvents.AsNoTracking().CountAsync(p => p.EventId == ProviderEventIds.ToGuid(evt.ProviderEventId)));
    }
}
