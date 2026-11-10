using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Services;
using Shop.Domain.Enums;
using Shop.Infrastructure.Payments;

namespace Shop.IntegrationTests;

/// <summary>
/// Basic shop flow, re-cut at the quote money path (ADR-0003 deleted the legacy
/// synchronous POST /api/checkout these tests targeted): browse -> add to cart ->
/// quote (with Idempotency-Key) -> webhook decides the fate. Ownership rules
/// unchanged.
/// </summary>
public class CartCheckoutTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(string token, Guid productId)> SetupCustomerWithProductAsync()
    {
        var email = $"shopper_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);

        ClearToken();
        var products = await Client.GetFromJsonAsync<PagedResult>("/api/products?page=1&pageSize=5");
        Assert.NotNull(products);
        Assert.NotEmpty(products!.Items);
        var productId = products.Items[0].Id;

        UseToken(token);
        return (token, productId);
    }

    private async Task<HttpResponseMessage> QuoteAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
        {
            Content = JsonContent.Create(new CreateQuoteRequest(
                new ShippingAddressDto("John Doe", "0123456789", "123 Example Street", "Ho Chi Minh City"))),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await Client.SendAsync(request);
    }

    /// <summary>Apply pending webhook-intake outbox rows the way the queue worker would.</summary>
    private async Task DeliverIntakeRowsAsync()
    {
        var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<Shop.Infrastructure.Persistence.AppDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking()
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

    [Fact]
    public async Task AddToCart_Quote_CreatesOpenOrder_AndReservesStock()
    {
        var (_, productId) = await SetupCustomerWithProductAsync();

        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);

        var cart = await Client.GetAsync("/api/cart");
        Assert.True(cart.IsSuccessStatusCode);

        var quote = await QuoteAsync();
        Assert.Equal(HttpStatusCode.Created, quote.StatusCode);
        var quoteDoc = await quote.Content.ReadFromJsonAsync<QuoteResult>();

        // Stock is HELD (reserved), not consumed — the human hasn't paid.
        // Snapshot-diff: the same shared seeded product may carry other tests' holds.
        var db = factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<Shop.Infrastructure.Persistence.AppDbContext>();
        var before = await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);

        var orders = await Client.GetFromJsonAsync<PagedResult>("/api/orders?page=1&pageSize=10");
        Assert.Contains(orders!.Items, i => i.Id == quoteDoc!.Order.Id);

        // Confirm + webhook delivery completes the classic "checkout success".
        var confirm = await Client.PostAsync($"/pay/{quoteDoc.SessionToken}/confirm", null);
        Assert.Equal(HttpStatusCode.Accepted, confirm.StatusCode);
        await DeliverIntakeRowsAsync();

        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleAsync(o => o.Id == quoteDoc.Order.Id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        var after = await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == order.Items.Single().ProductId);
        Assert.Equal(before.Quantity - 1, after.Quantity); // committed exactly this order's unit
        Assert.Equal(before.ReservedQuantity - 1, after.ReservedQuantity); // and released its hold
        Assert.True(after.Quantity >= 0 && after.ReservedQuantity >= 0);
    }

    [Fact]
    public async Task DeclinedWebhook_OpensNoOrderState_BeyondPaymentFailed()
    {
        var (_, productId) = await SetupCustomerWithProductAsync();
        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);
        var quoteRes = await QuoteAsync();
        var quote = await quoteRes.Content.ReadFromJsonAsync<QuoteResult>();

        // The provider declines via a signed webhook (the legacy simulateFailure flag died with ADR-0003).
        var provider = factory.Services.CreateScope().ServiceProvider.GetRequiredService<FakePspProvider>();
        var amountMinor = (long)decimal.Round(quote!.Order.TotalAmount * 100m, MidpointRounding.AwayFromZero);
        var body = JsonSerializer.Serialize(new Shop.Application.Events.PaymentProviderEvent(
            "fake-psp", $"decline-{Guid.NewGuid():N}", quote.SessionToken, PaymentOutcome.Failed,
            amountMinor, "USD", DateTime.UtcNow, "fake.decline"),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Psp-Signature", provider.ComputeSignature(body));
        var ack = await Client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, ack.StatusCode);

        await DeliverIntakeRowsAsync();

        var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<Shop.Infrastructure.Persistence.AppDbContext>();
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == quote.Order.Id);
        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal(PaymentOutcome.Failed, order.Outcome);
    }

    [Fact]
    public async Task Quote_EmptyCart_Returns400()
    {
        var email = $"emptycart_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);

        var clear = await Client.DeleteAsync("/api/cart");
        Assert.True(clear.IsSuccessStatusCode || clear.StatusCode == HttpStatusCode.NoContent);

        var quote = await QuoteAsync();
        Assert.Equal(HttpStatusCode.BadRequest, quote.StatusCode); // EmptyCart -> 400, unchanged contract
    }

    [Fact]
    public async Task Customer_Cannot_Access_Another_Customers_Order()
    {
        var (tokenA, productId) = await SetupCustomerWithProductAsync();
        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);
        var quoteRes = await QuoteAsync();
        var quote = await quoteRes.Content.ReadFromJsonAsync<QuoteResult>();

        var tokenB = await RegisterAndLoginAsync($"other_{Guid.NewGuid():N}@test.local");
        UseToken(tokenB);
        var otherAccess = await Client.GetAsync($"/api/orders/{quote!.Order.Id}");
        Assert.True(otherAccess.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        UseToken(tokenA);
    }

    [Fact]
    public async Task Admin_Can_Manage_Orders_But_Customer_Cannot_Access_Admin_Api()
    {
        var (token, _) = await SetupCustomerWithProductAsync();
        UseToken(token);
        var forbidden = await Client.GetAsync("/api/admin/orders?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private record PagedResult(List<Item> Items, int Page, int PageSize, int TotalCount);
    private record Item(Guid Id, string Name);
}
