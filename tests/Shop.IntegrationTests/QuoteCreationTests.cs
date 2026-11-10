using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.ValueObjects;

namespace Shop.IntegrationTests;

/// <summary>
/// B5: quote creation at the IQuoteService boundary — server repricing,
/// guarded reservations (B1), single Open Quote replace policy, session
/// capability, atomicity on failure, and legacy-pending immunity (ADR-0001).
/// Cart seeded through ICartService so repricing is the only surprise.
/// </summary>
public class QuoteCreationTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private static readonly ShippingAddressDto Address = new("Q T", "+1", "1 Quote Ln", "Town");
    private IQuoteService Quotes() => Resolve<IQuoteService>();
    private ICartService Carts() => Resolve<ICartService>();
    private T Resolve<T>() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();
    private Shop.Infrastructure.Persistence.AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<Shop.Infrastructure.Persistence.AppDbContext>();

    private async Task<(Guid userId, Guid productId)> SeedUserWithStockAsync(int stock, decimal price = 100m)
    {
        var db = NewDb();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"quote_{tag}@test.local", PasswordHash = "x", FirstName = "Q", LastName = "T", Role = UserRole.Customer };
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var product = new Product
        {
            Name = $"quote product {tag}", Slug = $"quote-{tag}", SKU = $"QRY-{tag}",
            CategoryId = categoryId, Price = price, Status = ProductStatus.Active,
            Inventory = new Inventory { Quantity = stock },
        };
        product.Inventory.ProductId = product.Id;
        db.Users.Add(user);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (user.Id, product.Id);
    }

    private async Task<QuoteResult> CreateAsync(Guid userId) =>
        await Quotes().CreateQuoteAsync(userId, new CreateQuoteRequest(Address));

    [Fact]
    public async Task CreateQuote_ReservesStock_AttachesSession_ReturnsBill()
    {
        var (userId, productId) = await SeedUserWithStockAsync(10, 100m);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 2)); // subtotal 200, shipping 15 -> 215

        var quote = await CreateAsync(userId);

        var db = NewDb();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == quote.Order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.True(order.IsQuote);
        Assert.InRange(order.ExpiresAt!.Value, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
        Assert.Equal(215m, order.TotalAmount);
        Assert.Equal(2, order.Items.Single().ReservedQuantity);

        var inv = await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);
        Assert.Equal(2, inv.ReservedQuantity);

        var session = await db.PaymentSessions.AsNoTracking().SingleAsync(s => s.OrderId == order.Id);
        Assert.Equal(quote.SessionToken, session.SessionToken);
        Assert.Equal(215m, session.Amount);
        Assert.Equal(quote.HostedPayUrl, $"/pay/{session.SessionToken}");

        // outbox: order.created enqueued atomically with the quote (M10 wiring)
        Assert.Equal(1, await db.OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == order.Id && m.Type == "order.created"));
    }

    [Fact]
    public async Task CreateQuote_RepricesAgainstStaleCartLine()
    {
        var (userId, productId) = await SeedUserWithStockAsync(10, 100m);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 1));

        var db = NewDb(); // price moved after the cart line was added — backend is the arbiter
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 120m));

        var quote = await CreateAsync(userId);

        Assert.Equal(120m + 15m, quote.Order.TotalAmount);
    }

    [Fact]
    public async Task CreateQuote_InsufficientStock_FailsAtomically_NoOrderNoReservation()
    {
        var (userId, productId) = await SeedUserWithStockAsync(5);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 5));

        var db = NewDb(); // someone ate the stock since the cart add
        await db.Inventories.Where(i => i.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, 2));

        await Assert.ThrowsAsync<InsufficientStockException>(async () => await CreateAsync(userId));

        var db2 = NewDb();
        Assert.Equal(0, await db2.Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
        var inv2 = await db2.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);
        Assert.Equal(0, inv2.ReservedQuantity);
        Assert.Equal(0, await db2.PaymentSessions.AsNoTracking().CountAsync(s => s.UserId == userId));
        // no order.created rows whose aggregate is one of this user's orders (there are none)
        Assert.Equal(0, await db2.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == "order.created" && db2.Orders.Any(o => o.UserId == userId && o.Id == m.AggregateId)));
    }

    [Fact]
    public async Task CreateQuote_EmptyCart_Rejected()
    {
        var (userId, _) = await SeedUserWithStockAsync(5);
        await Assert.ThrowsAsync<EmptyCartException>(async () => await CreateAsync(userId));
    }

    [Fact]
    public async Task SecondQuote_ReplacesFirst_ReleasesOldHoldsOnlyNew()
    {
        var (userId, productId) = await SeedUserWithStockAsync(10);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 2));
        var first = await CreateAsync(userId);

        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 1)); // human tweaks cart
        var second = await CreateAsync(userId);

        var db = NewDb();
        var old = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == first.Order.Id);
        Assert.Equal(OrderStatus.Cancelled, old.Status);
        Assert.NotEqual(first.Order.Id, second.Order.Id);

        var inv = await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);
        Assert.Equal(3, inv.ReservedQuantity); // exactly one quote's worth: 2 released, 3 held

        Assert.Equal(1, await db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.AggregateId == first.Order.Id && m.Type == "order.cancelled"));

        // At most one Open Quote per user (glossary invariant).
        var open = await db.Orders.AsNoTracking()
            .CountAsync(o => o.UserId == userId && o.Status == OrderStatus.Pending && o.ExpiresAt != null);
        Assert.Equal(1, open);
    }

    [Fact]
    public async Task Quote_NeverTouchesLegacyPendingOrders()
    {
        // ADR-0001 consequence: V1 Pendings have no expiry — not Open Quotes.
        var (userId, productId) = await SeedUserWithStockAsync(10);
        var db = NewDb();
        var legacy = new Order
        {
            UserId = userId, OrderNumber = Order.GenerateOrderNumber(), Status = OrderStatus.Pending,
            Subtotal = 1m, ShippingFee = 0m, TotalAmount = 1m,
            ShippingAddress = new ShippingAddress { FullName = "L", Phone = "1", Address = "a", City = "c" },
        };
        db.Orders.Add(legacy);
        await db.SaveChangesAsync();

        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 1));
        await CreateAsync(userId);

        var stillLegacy = await NewDb().Orders.AsNoTracking().SingleAsync(o => o.Id == legacy.Id);
        Assert.Equal(OrderStatus.Pending, stillLegacy.Status);
    }

    [Fact]
    public async Task CreateQuote_InactiveProductInCart_Rejected_NoOrder()
    {
        var (userId, productId) = await SeedUserWithStockAsync(10);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 1));
        await NewDb().Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProductStatus.Inactive));

        await Assert.ThrowsAsync<InactiveProductException>(async () => await CreateAsync(userId));
        Assert.Equal(0, await NewDb().Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
        var inv = await NewDb().Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId);
        Assert.Equal(0, inv.ReservedQuantity);
    }

    [Fact]
    public async Task GetOpenQuote_TracksTheActiveQuote()
    {
        var (userId, productId) = await SeedUserWithStockAsync(10);
        Assert.Null(await Quotes().GetOpenQuoteAsync(userId));

        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 1));
        var created = await CreateAsync(userId);

        var open = await Quotes().GetOpenQuoteAsync(userId);
        Assert.NotNull(open);
        Assert.Equal(created.Order.Id, open.Order.Id);
        Assert.Equal(created.SessionToken, open.SessionToken);
    }

    [Fact]
    public async Task CartIsNotClearedByQuoting()
    {
        // Plan §B5: "cart is NOT cleared yet" — clearing happens on capture.
        var (userId, productId) = await SeedUserWithStockAsync(10);
        await Carts().AddItemAsync(userId, new AddCartItemRequest(productId, 2));

        await CreateAsync(userId);

        var cart = await Carts().GetCartAsync(userId);
        Assert.Equal(2, cart.TotalQuantity);
    }
}
