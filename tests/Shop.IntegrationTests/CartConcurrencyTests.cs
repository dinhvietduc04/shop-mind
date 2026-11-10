using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B1 (cart lost update): 10 parallel add_to_cart(q=1) for the same product and user
/// must produce line quantity = 10 — not a silently dropped subset. This is the exact
/// shape of agent retry traffic (plan §2.2).
/// Seam: ICartService public boundary.
/// </summary>
public class CartConcurrencyTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private ICartService CartService() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<ICartService>();

    private async Task<(Guid userId, Guid productId)> SeedUserAndProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var tag = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Email = $"storm_{tag}@test.local",
            PasswordHash = "x",
            FirstName = "Storm",
            LastName = "Test",
            Role = UserRole.Customer,
        };
        var product = new Product
        {
            Name = $"Storm product {tag}",
            Slug = $"storm-{tag}",
            SKU = $"STORM-{tag}",
            CategoryId = categoryId,
            Price = 7m,
            Status = ProductStatus.Active,
            Inventory = new Inventory { Quantity = 1000 },
        };
        product.Inventory.ProductId = product.Id;

        db.Users.Add(user);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (user.Id, product.Id);
    }

    [Fact]
    public async Task AddItem_ConcurrentAddsSameProduct_FinalQuantityIsSum()
    {
        // Plan §B1 exit criterion: 10 parallel add_to_cart(q=1) → qty = 10, not fewer.
        var (userId, productId) = await SeedUserAndProductAsync();

        var adds = Enumerable.Range(0, 10).Select(async _ =>
        {
            var cart = CartService(); // own scope = own DbContext, like 10 concurrent requests
            return await cart.AddItemAsync(userId, new AddCartItemRequest(productId, 1));
        });
        await Task.WhenAll(adds);

        var final = await CartService().GetCartAsync(userId);
        var line = Assert.Single(final.Items);
        Assert.Equal(10, line.Quantity);
    }
}
