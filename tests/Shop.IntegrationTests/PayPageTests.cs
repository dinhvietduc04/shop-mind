using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Events;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Payments;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// B5 ADR-0002 at the HTTP edge: the capability-token bill view (anonymous,
/// never logged), human-only confirmation (owner + no agent scope), and the
/// webhook intake (verify raw-body HMAC -> enqueue -> 202; refusal -> 401 with
/// nothing enqueued). Confirmation enqueues; it does NOT mark paid — applying
/// fates is the consumer's job (tested in PaymentWebhookConsumerTests).
/// </summary>
public class PayPageTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<(Guid userId, string token, QuoteResult quote)> QuoteForNewUserAsync()
    {
        var email = $"pay_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        var db = NewDb();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Status == ProductStatus.Active);

        ClearToken();
        UseToken(token);
        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<ICartService>()
            .AddItemAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        var quote = await factory.Services.CreateScope().ServiceProvider.GetRequiredService<IQuoteService>()
            .CreateQuoteAsync(user.Id, new CreateQuoteRequest(new ShippingAddressDto("P A", "1", "a", "c")));
        return (user.Id, token, quote);
    }

    [Fact]
    public async Task ViewBill_ByCapabilityTokenOnly_Anonymous200()
    {
        var (_, _, quote) = await QuoteForNewUserAsync();

        ClearToken();
        var res = await Client.GetAsync($"/pay/{quote.SessionToken}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var bill = await res.Content.ReadFromJsonAsync<PayBillView>();
        Assert.Equal("Pending", bill!.OrderStatus);
        Assert.Equal(quote.Order.TotalAmount, bill.Total);
        Assert.Single(bill.Items);
        Assert.Null(bill.Outcome);
    }

    [Fact]
    public async Task ViewBill_UnknownToken_404()
    {
        ClearToken();
        var res = await Client.GetAsync("/pay/nonexistent-token");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task PayPageToken_NeverAppearsInRequestLogs()
    {
        // ADR-0002 exit criterion: /pay/* excluded from request logging.
        var before = TestLogSink.Instance.Messages.Count;
        var (_, _, quote) = await QuoteForNewUserAsync();

        ClearToken();
        await Client.GetAsync($"/pay/{quote.SessionToken}");
        await Client.GetAsync("/api/categories"); // control: ordinary paths ARE logged

        var lines = TestLogSink.Instance.Messages.ToArray();
        // The token (and the full URL containing it) must never appear; the
        // redacted "HTTP GET /pay/* -> 200" line is exactly the intended shape.
        Assert.DoesNotContain(quote.SessionToken, string.Join("\n", lines));
        Assert.DoesNotContain($"/pay/{quote.SessionToken}", string.Join("\n", lines));
        Assert.Contains(lines, l => l.Contains("/api/categories"));
    }

    [Fact]
    public async Task Confirm_Owner_EnqueuesProviderWebhook_DoesNotMarkPaid()
    {
        var (_, _, quote) = await QuoteForNewUserAsync();

        var res = await Client.PostAsync($"/pay/{quote.SessionToken}/confirm", null);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var db = NewDb();
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == quote.Order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status); // the webhook will decide — ADR-0003
        Assert.Equal(1, await db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "payment.webhook-received"));
    }

    [Fact]
    public async Task Confirm_WrongUser_403()
    {
        var (_, _, quote) = await QuoteForNewUserAsync();
        var before = await NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "payment.webhook-received");

        var otherToken = await RegisterAndLoginAsync($"intruder_{Guid.NewGuid():N}@test.local");
        UseToken(otherToken);
        var res = await Client.PostAsync($"/pay/{quote.SessionToken}/confirm", null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var after = await NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "payment.webhook-received");
        Assert.Equal(before, after); // the intruder enqueued nothing
    }

    [Fact]
    public async Task Confirm_AfterCancel_422()
    {
        var (userId, token, quote) = await QuoteForNewUserAsync();
        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<IOrderService>()
            .CancelMineAsync(userId, quote.Order.Id);

        UseToken(token);
        var res = await Client.PostAsync($"/pay/{quote.SessionToken}/confirm", null);

        Assert.Equal(HttpStatusCode.UnprocessableContent, res.StatusCode);
    }
}

/// <summary>Webhook intake: HMAC over the raw body, 202-acknowledge-enqueue, 401-refuse-nothing.</summary>
public class WebhookIntakeTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
    private FakePspProvider Provider() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<FakePspProvider>();

    private async Task<string> SessionTokenAsync()
    {
        var email = $"wh_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        var db = NewDb();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Status == ProductStatus.Active);
        UseToken(token);
        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<ICartService>()
            .AddItemAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<IQuoteService>()
            .CreateQuoteAsync(user.Id, new CreateQuoteRequest(new ShippingAddressDto("W H", "1", "a", "c")));
        return (await db.PaymentSessions.AsNoTracking().FirstAsync(s => s.UserId == user.Id)).SessionToken;
    }

    private (string Body, Shop.Application.Events.PaymentProviderEvent Evt) MakeBody(string sessionToken, PaymentOutcome outcome = PaymentOutcome.Captured, long amountMinor = 11500)
    {
        var evt = new PaymentProviderEvent(
            "fake-psp", $"evt-{Guid.NewGuid():N}", sessionToken, outcome, amountMinor, "USD",
            DateTime.UtcNow, "fake.checkout.session.completed");
        return (JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), evt);
    }

    private async Task<(HttpResponseMessage Res, string Body)> PostWebhookAsync(string body, string? signature)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null) message.Headers.Add("X-Psp-Signature", signature);
        var res = await Client.SendAsync(message);
        return (res, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ValidSignature_Enqueues_And202()
    {
        var token = await SessionTokenAsync();
        var (body, evt) = MakeBody(token);
        var sig = Provider().ComputeSignature(body);

        var (res, _) = await PostWebhookAsync(body, sig);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var intakeId = Shop.Domain.ProviderEventIds.ToGuid(evt.ProviderEventId);
        Assert.Equal(1, await NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == intakeId));
    }

    [Fact]
    public async Task BadSignature_401_NothingEnqueued()
    {
        var token = await SessionTokenAsync();
        var (body, evt) = MakeBody(token);

        var (res, _) = await PostWebhookAsync(body, Convert.ToHexString(Enumerable.Repeat((byte)0xAB, 32).ToArray()));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var intakeId = Shop.Domain.ProviderEventIds.ToGuid(evt.ProviderEventId);
        Assert.Equal(0, await NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == intakeId));
    }

    [Fact]
    public async Task TamperedBody_401()
    {
        var token = await SessionTokenAsync();
        var (body, evt) = MakeBody(token);
        var sig = Provider().ComputeSignature(body);

        // Flip a field the signature does cover: outcome number -> different number.
        var tampered = body.Replace("\"outcome\":0", "\"outcome\":1");
        Assert.NotEqual(body, tampered); // guard: the tamper actually bites
        var (res, _) = await PostWebhookAsync(tampered, sig);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var intakeId = Shop.Domain.ProviderEventIds.ToGuid(evt.ProviderEventId);
        Assert.Equal(0, await NewDb().OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == intakeId));
    }
}
