using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.IntegrationTests;

/// <summary>
/// B1 (race-safe stock): guarded reservation primitives on IInventoryService.
/// Seam: the public Application service boundary — each call is one atomic DB
/// statement, so concurrent callers can never interleave check and write.
/// </summary>
public class StockReservationTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    /// <summary>Fixture setup: create a product with exactly this much physical stock.</summary>
    private async Task<Guid> SeedProductWithStockAsync(int quantity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var tag = Guid.NewGuid().ToString("N");
        var product = new Product
        {
            Name = $"Race-test product {tag}",
            Slug = $"race-{tag}",
            SKU = $"RACE-{tag}",
            CategoryId = categoryId,
            Price = 10m,
            Status = ProductStatus.Active,
            Inventory = new Inventory { Quantity = quantity, ReservedQuantity = 0 },
        };
        product.Inventory.ProductId = product.Id;
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private IInventoryService InventoryService()
        => factory.Services.CreateScope().ServiceProvider.GetRequiredService<IInventoryService>();

    [Fact]
    public async Task Reserve_RejectsQuantityAboveAvailable_LeavesStockUntouched()
    {
        var productId = await SeedProductWithStockAsync(5);
        var inventory = InventoryService();

        var reserved = await inventory.TryReserveAsync(productId, 6);

        Assert.False(reserved);
        var stock = await inventory.GetByProductIdAsync(productId);
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(5, stock.AvailableQuantity);
    }

    [Fact]
    public async Task Reserve_OversellStorm_ExactlyAvailableCountSucceed()
    {
        // Plan §B1 exit criterion: 5 units, 20 parallel reserves of 1 → exactly 5 winners.
        var productId = await SeedProductWithStockAsync(5);

        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            var inventory = InventoryService(); // own scope = own DbContext, like 20 HTTP requests
            return await inventory.TryReserveAsync(productId, 1);
        });
        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(ok => ok));
        var stock = await InventoryService().GetByProductIdAsync(productId);
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(5, stock.ReservedQuantity);
        Assert.Equal(0, stock.AvailableQuantity);
    }

    [Fact]
    public async Task Commit_ReservesThenPaid_ConsumesStockAndReservation()
    {
        var productId = await SeedProductWithStockAsync(5);
        var inventory = InventoryService();
        Assert.True(await inventory.TryReserveAsync(productId, 5));

        var committed = await inventory.TryCommitReservationAsync(productId, 5);

        Assert.True(committed);
        var stock = await inventory.GetByProductIdAsync(productId);
        Assert.Equal(0, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public async Task Commit_RejectsMoreThanReserved_LeavesStockUntouched()
    {
        var productId = await SeedProductWithStockAsync(5);
        var inventory = InventoryService();
        Assert.True(await inventory.TryReserveAsync(productId, 3));

        var committed = await inventory.TryCommitReservationAsync(productId, 4);

        Assert.False(committed);
        var stock = await inventory.GetByProductIdAsync(productId);
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(3, stock.ReservedQuantity);
    }

    [Fact]
    public async Task Release_ReturnsReservedStockToAvailable()
    {
        var productId = await SeedProductWithStockAsync(5);
        var inventory = InventoryService();
        Assert.True(await inventory.TryReserveAsync(productId, 3));

        var released = await inventory.TryReleaseReservationAsync(productId, 3);

        Assert.True(released);
        var stock = await inventory.GetByProductIdAsync(productId);
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(5, stock.AvailableQuantity);
    }

    [Fact]
    public async Task Release_RejectsMoreThanReserved_LeavesStockUntouched()
    {
        var productId = await SeedProductWithStockAsync(5);
        var inventory = InventoryService();
        Assert.True(await inventory.TryReserveAsync(productId, 3));

        var released = await inventory.TryReleaseReservationAsync(productId, 4);

        Assert.False(released);
        var stock = await inventory.GetByProductIdAsync(productId);
        Assert.Equal(3, stock.ReservedQuantity);
    }

    [Fact]
    public async Task LateCommitVsRelease_Race_ExactlyOneWins()
    {
        // Quote expiry racing a late payment-success webhook: the same 5-unit
        // reservation may be committed OR released, never both.
        var productId = await SeedProductWithStockAsync(5);
        Assert.True(await InventoryService().TryReserveAsync(productId, 5));

        var ops = new[]
        {
            InventoryService().TryCommitReservationAsync(productId, 5),
            InventoryService().TryReleaseReservationAsync(productId, 5),
        };
        var results = await Task.WhenAll(ops);

        Assert.Equal(1, results.Count(ok => ok));
        var stock = await InventoryService().GetByProductIdAsync(productId);
        Assert.Equal(0, stock.ReservedQuantity); // either fate clears the reservation
        Assert.Contains(stock.Quantity, new[] { 0, 5 }); // 0 = commit won, 5 = release won
    }
}
