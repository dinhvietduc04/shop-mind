using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B1 exit criterion, at the HTTP edge: five users race to checkout a product
/// holding three units. The guarded one-statement decrement means exactly three
/// orders become Paid, two get a typed 409 — never four winners, never negative
/// stock, never a silent lost update. This is the shape of the agent-retry and
/// flash-sale traffic the reserve primitives must survive from B5 onward.
/// Seam: the public REST surface (full middleware stack, disposable Postgres).
/// </summary>
public class CheckoutConcurrencyTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private static readonly ShippingAddressDto Addr = new("Race Tester", "+1-555-0000", "1 Concurrency Ln", "Vienna");

    private async Task<HttpResponseMessage> PostAuthedAsync(string url, object body, string token)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(msg);
    }

    private async Task<Guid> SeedProductWithStockAsync(int quantity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var tag = Guid.NewGuid().ToString("N");
        var product = new Product
        {
            Name = $"Flash sale {tag}",
            Slug = $"flash-{tag}",
            SKU = $"FLASH-{tag}",
            CategoryId = categoryId,
            Price = 10m,
            Status = ProductStatus.Active,
            Inventory = new Inventory { Quantity = quantity },
        };
        product.Inventory.ProductId = product.Id;
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    [Fact]
    public async Task ParallelCheckouts_FiveUsersThreeUnits_ExactlyThreeWinAndStockNeverGoesNegative()
    {
        var productId = await SeedProductWithStockAsync(3);
        var runTag = Guid.NewGuid().ToString("N");

        // Prepare: five distinct users, each cart already holds one of the last units.
        var tokens = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var token = await RegisterAndLoginAsync($"flash_{runTag}_{i}@test.local");
            var cartRes = await PostAuthedAsync("/api/cart/items", new AddCartItemRequest(productId, 1), token);
            Assert.True(cartRes.IsSuccessStatusCode, $"cart prep failed: {(int)cartRes.StatusCode}");
            tokens.Add(token);
        }

        // Storm: all five checkouts in flight at once.
        var responses = await Task.WhenAll(tokens.Select(t => PostAuthedAsync("/api/checkout", new CheckoutRequest(Addr), t)));

        Assert.Equal(3, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(2, responses.Count(r => (int)r.StatusCode == 409));

        // Database truth: three units sold, zero left, nothing reserved.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var inv = await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);
        Assert.Equal(0, inv.Quantity);
        Assert.Equal(0, inv.ReservedQuantity);

        var paid = await db.Orders.AsNoTracking()
            .CountAsync(o => o.Status == OrderStatus.Paid && o.Items.Any(it => it.ProductId == productId));
        Assert.Equal(3, paid);
    }
}
