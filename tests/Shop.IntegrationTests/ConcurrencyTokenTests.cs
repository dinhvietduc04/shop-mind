using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B1: xmin optimistic concurrency tokens. Until a token exists, EF can never raise
/// DbUpdateConcurrencyException — the recovery paths that catch it (CartService) are
/// dead code and lost updates are silent. These tests pin that the token is live
/// (losing writer throws) before the retry loop lands on top of it.
/// Seam: IAppDbContext (the persistence boundary every Application service shares).
/// </summary>
public class ConcurrencyTokenTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private IServiceScope NewScope() => factory.Services.CreateScope();

    [Fact]
    public async Task ConcurrentCartItemWrites_LosingWriter_SeesDbUpdateConcurrencyException()
    {
        Guid cartItemId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var email = $"xmin_{Guid.NewGuid():N}@test.local";
            var user = new User
            {
                Email = email,
                PasswordHash = "x",
                FirstName = " xmin",
                LastName = "test",
                Role = UserRole.Customer,
            };
            var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
            var product = new Product
            {
                Name = "xmin product",
                Slug = $"xmin-{Guid.NewGuid():N}",
                SKU = $"XMIN-{Guid.NewGuid():N}",
                CategoryId = categoryId,
                Price = 5m,
                Status = ProductStatus.Active,
                Inventory = new Inventory { Quantity = 100 },
            };
            product.Inventory.ProductId = product.Id;
            var cart = new Cart { UserId = user.Id };
            var item = new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 5m };
            db.Users.Add(user);
            db.Products.Add(product);
            db.Carts.Add(cart);
            db.CartItems.Add(item);
            await db.SaveChangesAsync();
            cartItemId = item.Id;
        }

        // Two independent tracked reads of the same row (two concurrent requests).
        using var scopeA = NewScope();
        using var scopeB = NewScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<IAppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<IAppDbContext>();
        var itemB = await dbB.CartItems.SingleAsync(i => i.Id == cartItemId); // B loads first
        var itemA = await dbA.CartItems.SingleAsync(i => i.Id == cartItemId); // A loads too

        itemA.Quantity = 10;
        await dbA.SaveChangesAsync(); // A wins

        itemB.Quantity = 20;
        var act = async () => await dbB.SaveChangesAsync(); // B's write is stale

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(act);
    }
}
