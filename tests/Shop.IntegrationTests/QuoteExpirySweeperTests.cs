using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Services;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B5: the expiry sweeper — cancels expired Open Quotes with release +
/// server-stated expired outcome, leaves un-expired and legacy orders alone,
/// and loses gracefully to a late webhook that won the transition guard first
/// (conditional updates: exactly one fate, never a double release).
/// </summary>
public class QuoteExpirySweeperTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private readonly QuoteHarness _h = new(factory);
    private QuoteExpirySweeper Sweeper() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<QuoteExpirySweeper>();

    [Fact]
    public async Task Sweep_CancelsExpiredReleasesStock_IgnoresFreshAndLegacy()
    {
        var expired1 = await _h.OpenQuoteAsync();
        var expired2 = await _h.OpenQuoteAsync();
        var fresh = await _h.OpenQuoteAsync();

        // A legacy V1 Pending (no expiry) must be invisible to the sweeper.
        var legacyUser = (await _h.OpenQuoteAsync()).UserId;
        var db = _h.NewDb();
        var legacy = new Shop.Domain.Entities.Order
        {
            UserId = legacyUser,
            OrderNumber = Shop.Domain.Entities.Order.GenerateOrderNumber(),
            Status = OrderStatus.Pending, // ExpiresAt left null = legacy shape
            Subtotal = 1m, ShippingFee = 0m, TotalAmount = 1m,
            ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress { FullName = "L G", Phone = "1", Address = "a", City = "c" },
        };
        db.Orders.Add(legacy);
        await db.SaveChangesAsync();

        var past = DateTime.UtcNow.AddMinutes(-1);
        _h.BackdateQuote(expired1.Quote.Order.Id, past);
        _h.BackdateQuote(expired2.Quote.Order.Id, past);

        var count = await Sweeper().SweepAsync(DateTime.UtcNow);

        Assert.Equal(2, count);
        Assert.Equal(OrderStatus.Cancelled, (await _h.OrderAsync(expired1.Quote.Order.Id)).Status);
        Assert.Equal(PaymentOutcome.Expired, (await _h.OrderAsync(expired1.Quote.Order.Id)).Outcome);
        Assert.Equal(0, (await _h.StockAsync(expired1.ProductId)).ReservedQuantity);
        Assert.Equal(10, (await _h.StockAsync(expired1.ProductId)).Quantity);
        Assert.Equal(OrderStatus.Cancelled, (await _h.OrderAsync(expired2.Quote.Order.Id)).Status);

        Assert.Equal(OrderStatus.Pending, (await _h.OrderAsync(fresh.Quote.Order.Id)).Status); // untouched
        Assert.Equal(OrderStatus.Pending, (await _h.OrderAsync(legacy.Id)).Status);           // legacy invisible
        Assert.Equal(2, await _h.NewDb().OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "order.cancelled" && (m.AggregateId == expired1.Quote.Order.Id || m.AggregateId == expired2.Quote.Order.Id)));
    }

    [Fact]
    public async Task SweepTwice_SecondPassIsNoop_NeverDoubleReleases()
    {
        var o = await _h.OpenQuoteAsync();
        _h.BackdateQuote(o.Quote.Order.Id, DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal(1, await Sweeper().SweepAsync(DateTime.UtcNow));
        Assert.Equal(0, await Sweeper().SweepAsync(DateTime.UtcNow));

        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(10, stock.Quantity); // released once, not +1 again
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public async Task LateCaptureWinsThenSweep_SweeperSkipsPaidOrder()
    {
        // Sweep races a late "succeeded": the capture applies first; the sweeper's
        // conditional guard must then SKIP the paid order (never cancel a paid
        // quote, never release committed stock).
        var o = await _h.OpenQuoteAsync();
        _h.BackdateQuote(o.Quote.Order.Id, DateTime.UtcNow.AddMinutes(-1));

        await factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<PaymentWebhookConsumer>()
            .ApplyAsync(new Shop.Application.Events.PaymentProviderEvent(
                "fake-psp", $"evt-{Guid.NewGuid():N}", o.Quote.SessionToken, PaymentOutcome.Captured,
                (long)(o.Total * 100m), "USD", DateTime.UtcNow, "late-capture"));

        var swept = await Sweeper().SweepAsync(DateTime.UtcNow);

        Assert.Equal(0, swept);
        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentOutcome.Captured, order.Outcome);
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(9, stock.Quantity);   // committed stayed
        Assert.Equal(0, stock.ReservedQuantity);
    }
}
