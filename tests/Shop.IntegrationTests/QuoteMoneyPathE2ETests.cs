using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Services;
using Shop.Domain;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// B5 exit criteria, scripted end-to-end over HTTP + the consumer (the outbox
/// intake rows are delivered exactly as the queue worker would deliver them):
/// quote -> reservations visible -> human confirms on the fake hosted page ->
/// Paid, stock committed, quoted cart lines cleared. Expiry path cancels +
/// releases. Replay x3 of the SAME provider event applies ONE transition.
/// The oversell storm that guarded legacy checkout (5 users / 3 units) now
/// guards the QUOTE path — the one money path (ADR-0003).
/// </summary>
public class QuoteMoneyPathE2ETests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private readonly QuoteHarness _h = new(factory);
    private AppDbContext NewDb() => _h.NewDb();

    private async Task DeliverIntakeRowsAsync()
    {
        // Simulate the queue worker: pull payment.webhook-received outbox rows
        // (oldest first) and apply each through the consumer, like RabbitMQ delivery.
        var rows = await NewDb().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "payment.webhook-received")
            .OrderBy(m => m.OccurredAt)
            .ToListAsync();
        foreach (var row in rows)
        {
            using var doc = JsonDocument.Parse(row.Payload);
            var d = doc.RootElement.GetProperty("data");
            var evt = new Shop.Application.Events.PaymentProviderEvent(
                d.GetProperty("provider").GetString()!, d.GetProperty("providerEventId").GetString()!,
                d.GetProperty("sessionToken").GetString()!,
                Enum.Parse<PaymentOutcome>(d.GetProperty("outcome").GetString()!),
                d.GetProperty("amountMinor").GetInt64(), d.GetProperty("currency").GetString()!,
                d.GetProperty("occurredAt").GetDateTime(), d.GetProperty("rawType").GetString()!);

            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PaymentWebhookConsumer>().ApplyAsync(evt);
        }
    }

    private async Task<(string token, Guid userId, Guid productId)> RegisteredUserWithCartAsync(int qty = 1)
    {
        var db = NewDb();
        var email = $"e2e_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Status == ProductStatus.Active && p.Inventory!.Quantity >= qty);
        UseToken(token);
        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<Shop.Application.Interfaces.ICartService>()
            .AddItemAsync(user.Id, new AddCartItemRequest(product.Id, qty));
        return (token, user.Id, product.Id);
    }

    private async Task<HttpResponseMessage> QuoteAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
        {
            Content = JsonContent.Create(new CreateQuoteRequest(new ShippingAddressDto("E2E User", "+1-555", "1 Main", "Springfield"))),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task HumanPath_Quote_View_Confirm_Deliver_Paid()
    {
        var (token, userId, _) = await RegisteredUserWithCartAsync(2);
        var quoteRes = await QuoteAsync();
        Assert.Equal(HttpStatusCode.Created, quoteRes.StatusCode);
        var quote = await quoteRes.Content.ReadFromJsonAsync<QuoteResult>();

        // Reservation visible: stock set aside, not consumed.
        var quotedOrder = await _h.OrderAsync(quote!.Order.Id);
        var inv = await NewDb().Inventories.AsNoTracking().SingleAsync(i => i.ProductId == quotedOrder.Items.Single().ProductId);
        Assert.Equal(2, inv.ReservedQuantity);

        // Bill view by capability token, anonymously.
        var beforeAuth = Client.DefaultRequestHeaders.Authorization;
        Client.DefaultRequestHeaders.Authorization = null;
        var bill = await Client.GetAsync(quote.HostedPayUrl);
        Assert.Equal(HttpStatusCode.OK, bill.StatusCode);
        var billView = await bill.Content.ReadFromJsonAsync<PayBillView>();
        Assert.Equal("Pending", billView!.OrderStatus);
        Assert.Null(billView.Outcome);
        Client.DefaultRequestHeaders.Authorization = beforeAuth;

        // Human confirms on the hosted page; confirmation alone changes NO money state.
        var confirm = await Client.PostAsync($"/pay/{quote.SessionToken}/confirm", null);
        Assert.Equal(HttpStatusCode.Accepted, confirm.StatusCode);
        Assert.Equal(OrderStatus.Pending, (await _h.OrderAsync(quote.Order.Id)).Status);

        // ...the webhook (here: the intake row, delivered as the worker would) applies it.
        await DeliverIntakeRowsAsync();

        var order = await _h.OrderAsync(quote.Order.Id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentOutcome.Captured, order.Outcome);
        Assert.Equal(PaymentStatus.Succeeded, order.Payment?.Status);
        inv = await NewDb().Inventories.AsNoTracking().SingleAsync(i => i.ProductId == order.Items.Single().ProductId);
        Assert.Equal(0, inv.ReservedQuantity); // committed
        var cart = await _h.CartAsync(userId);
        Assert.Empty(cart!.Items); // quoted lines cleared
        Assert.Equal(1, await NewDb().OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "order.paid" && m.AggregateId == order.Id));
    }

    [Fact]
    public async Task ExpirePath_OpenQuote_SweptToCancelledAndReleased()
    {
        var o = await _h.OpenQuoteAsync(stock: 5, qty: 2);
        _h.BackdateQuote(o.Quote.Order.Id, DateTime.UtcNow.AddMinutes(-1));

        var swept = await factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<QuoteExpirySweeper>().SweepAsync(DateTime.UtcNow);

        Assert.True(swept >= 1);
        var order = await _h.OrderAsync(o.Quote.Order.Id);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentOutcome.Expired, order.Outcome);
        var stock = await _h.StockAsync(o.ProductId);
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public async Task SameWebhookEventDeliveredThreeTimes_SingleTransition()
    {
        var o = await _h.OpenQuoteAsync();
        var evt = new Shop.Application.Events.PaymentProviderEvent(
            "fake-psp", "evt-e2e-triple", o.Quote.SessionToken, PaymentOutcome.Captured,
            (long)(o.Total * 100m), "USD", DateTime.UtcNow, "e2e");

        var outcomes = new List<WebhookApplyOutcome>();
        for (var i = 0; i < 3; i++)
        {
            using var scope = factory.Services.CreateScope();
            outcomes.Add(await scope.ServiceProvider.GetRequiredService<PaymentWebhookConsumer>().ApplyAsync(evt));
        }

        Assert.Equal(WebhookApplyOutcome.Applied, outcomes[0]);
        Assert.Equal(WebhookApplyOutcome.Duplicate, outcomes[1]);
        Assert.Equal(WebhookApplyOutcome.Duplicate, outcomes[2]);
        Assert.Equal(OrderStatus.Paid, (await _h.OrderAsync(o.Quote.Order.Id)).Status);
        Assert.Equal(1, await NewDb().OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "order.paid" && m.AggregateId == o.Quote.Order.Id));
    }

    [Fact]
    public async Task QuoteStorm_FiveUsersThreeUnits_ExactlyThreeHoldStock_TwoTypedRefusals()
    {
        // Re-cut of the B1 exit criterion at the new money path: the guarded
        // reserve serializes five concurrent quote creations; oversell is
        // impossible and each refusal is the typed 409.
        var db = NewDb();
        var tag = Guid.NewGuid().ToString("N");
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var product = new Shop.Domain.Entities.Product
        {
            Name = $"storm {tag}", Slug = $"storm-{tag}", SKU = $"STM-{tag}", CategoryId = categoryId,
            Price = 20m, Status = ProductStatus.Active,
            Inventory = new Shop.Domain.Entities.Inventory { Quantity = 3 },
        };
        product.Inventory.ProductId = product.Id;
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var tokens = new List<string>();
        foreach (var i in Enumerable.Range(0, 5))
        {
            var email = $"storm_{tag}_{i}@test.local";
            var t = await RegisterAndLoginAsync(email);
            var u = await NewDb().Users.AsNoTracking().SingleAsync(x => x.Email == email);
            await factory.Services.CreateScope().ServiceProvider
                .GetRequiredService<Shop.Application.Interfaces.ICartService>()
                .AddItemAsync(u.Id, new AddCartItemRequest(product.Id, 1));
            tokens.Add(t);
        }

        var responses = await Task.WhenAll(tokens.Select(async t =>
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
            {
                Content = JsonContent.Create(new CreateQuoteRequest(
                    new ShippingAddressDto("Storm Buyer", "+1-555", "1 Ln", "Town"))),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await client.SendAsync(request);
        }));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict)); // InsufficientStock 409
        var inv = await NewDb().Inventories.AsNoTracking().SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(3, inv.Quantity);
        Assert.Equal(3, inv.ReservedQuantity); // exactly the stock; never more
    }
}
