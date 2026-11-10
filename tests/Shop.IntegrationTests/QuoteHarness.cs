using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.ValueObjects;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// Shared B5 fixture: fresh user -> product with stock -> cart -> open quote.
/// Gives every money-fate test a deterministic (user, product, quote, session).
/// </summary>
public sealed class QuoteHarness(ShopWebFactory factory)
{
    public record Opened(Guid UserId, Guid ProductId, QuoteResult Quote, decimal Total);

    public AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task<Opened> OpenQuoteAsync(int stock = 10, decimal price = 50m, int qty = 1)
    {
        var tag = Guid.NewGuid().ToString("N");
        var db = NewDb();
        var user = new User { Email = $"harness_{tag}@test.local", PasswordHash = "x", FirstName = "H", LastName = "Q", Role = UserRole.Customer };
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var product = new Product
        {
            Name = $"h {tag}", Slug = $"h-{tag}", SKU = $"H-{tag}", CategoryId = categoryId,
            Price = price, Status = ProductStatus.Active,
            Inventory = new Inventory { Quantity = stock },
        };
        product.Inventory.ProductId = product.Id;
        db.Users.Add(user);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        await factory.Services.CreateScope().ServiceProvider.GetRequiredService<ICartService>()
            .AddItemAsync(user.Id, new AddCartItemRequest(product.Id, qty));

        var quote = await factory.Services.CreateScope().ServiceProvider.GetRequiredService<IQuoteService>()
            .CreateQuoteAsync(user.Id, new CreateQuoteRequest(new ShippingAddressDto("H Q", "1", "a", "c")));
        return new Opened(user.Id, product.Id, quote, quote.Order.TotalAmount);
    }

    public async Task<Inventory> StockAsync(Guid productId) =>
        await NewDb().Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);

    public async Task<Order> OrderAsync(Guid orderId) =>
        await NewDb().Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Payment)
            .SingleAsync(o => o.Id == orderId);

    public async Task<Cart?> CartAsync(Guid userId) =>
        await NewDb().Carts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);

    public void BackdateQuote(Guid orderId, DateTime expiresAt) =>
        NewDb().Orders.Where(o => o.Id == orderId).ExecuteUpdate(s => s.SetProperty(o => o.ExpiresAt, expiresAt));
}
