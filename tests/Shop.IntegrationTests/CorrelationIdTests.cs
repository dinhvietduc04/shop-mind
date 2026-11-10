using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// M10: X-Correlation-Id middleware — incoming header reused, missing header
/// generated, echoed on the response, and propagated into outbox payloads via
/// the async request context (request → logs → outbox → future MCP trace).
/// Seam: HTTP edge + outbox row content.
/// </summary>
public class CorrelationIdTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private const string Header = "X-Correlation-Id";

    [Fact]
    public async Task IncomingCorrelationId_IsEchoedBack()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        request.Headers.Add(Header, "unit-corr-1");

        var res = await Client.SendAsync(request);

        Assert.Equal("unit-corr-1", res.Headers.GetValues(Header).Single());
    }

    [Fact]
    public async Task MissingCorrelationId_IsGeneratedPerRequest()
    {
        var a = await Client.GetAsync("/api/categories");
        var b = await Client.GetAsync("/api/categories");

        var idA = a.Headers.GetValues(Header).Single();
        var idB = b.Headers.GetValues(Header).Single();
        Assert.True(Guid.TryParse(idA, out _));
        Assert.True(Guid.TryParse(idB, out _));
        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public async Task CorrelationId_FlowsIntoOutboxPayload_EndToEnd()
    {
        // Seed a Pending order for a real registered user, cancel it over HTTP with
        // a correlation id — the emitted event must carry it.
        var email = $"corr_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);

        Guid orderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var order = new Order
            {
                UserId = user.Id,
                OrderNumber = Order.GenerateOrderNumber(),
                Status = OrderStatus.Pending,
                Subtotal = 3m, ShippingFee = 0m, TotalAmount = 3m,
                ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
                {
                    FullName = "C I", Phone = "1", Address = "a", City = "c",
                },
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        cancel.Headers.Add(Header, "corr-e2e-99");

        var res = await Client.SendAsync(cancel);
        res.EnsureSuccessStatusCode();

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db2.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == orderId);
        using var doc = System.Text.Json.JsonDocument.Parse(row.Payload);
        Assert.Equal("corr-e2e-99", doc.RootElement.GetProperty("correlationId").GetString());
    }
}
