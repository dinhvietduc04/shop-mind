using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.IntegrationTests;

/// <summary>
/// M10 flow wiring: state-changing durable flows enqueue catalog events in the
/// same transaction as their business rows — and ONLY on real transitions
/// (an ADR-0005 replay must not re-fire events). Legacy checkout is deliberately
/// unwired (deleted at B5, where created/paid/payment.failed move to the quote flow).
/// Seam: IOrderService / IProductService boundaries + OutboxMessages assertions.
/// </summary>
public class OutboxFlowTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private IServiceScope NewScope() => factory.Services.CreateScope();

    private async Task<(Guid userId, Guid orderId, Guid paymentId)> SeedPaidOrderAsync(string? transactionId = "txn-seed")
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Email = $"outbox_{tag}@test.local",
            PasswordHash = "x",
            FirstName = "O", LastName = "Box",
            Role = UserRole.Customer,
        };
        var order = new Order
        {
            UserId = user.Id,
            OrderNumber = Order.GenerateOrderNumber(),
            Status = OrderStatus.Paid,
            Subtotal = 99m,
            ShippingFee = 0m,
            TotalAmount = 99m,
            ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
            {
                FullName = "O B", Phone = "1", Address = "a", City = "c",
            },
        };
        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = 99m,
            Status = PaymentStatus.Succeeded,
            TransactionId = transactionId,
        };
        db.Users.Add(user);
        db.Orders.Add(order);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (user.Id, order.Id, payment.Id);
    }

    private async Task<(Guid userId, Guid orderId)> SeedPendingOrderAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Email = $"outbox_{tag}@test.local",
            PasswordHash = "x",
            FirstName = "O", LastName = "Box",
            Role = UserRole.Customer,
        };
        var order = new Order
        {
            UserId = user.Id,
            OrderNumber = Order.GenerateOrderNumber(),
            Status = OrderStatus.Pending,
            Subtotal = 25m,
            ShippingFee = 0m,
            TotalAmount = 25m,
            ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
            {
                FullName = "O B", Phone = "1", Address = "a", City = "c",
            },
        };
        db.Users.Add(user);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (user.Id, order.Id);
    }

    private async Task<List<OutboxMessage>> RowsForAsync(Guid orderId)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == orderId).ToListAsync();
    }

    [Fact]
    public async Task Cancel_EmitsOrderCancelled_Replay_EmitsNothingMore()
    {
        var (userId, orderId) = await SeedPendingOrderAsync();

        using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IOrderService>().CancelMineAsync(userId, orderId);

        var rows = await RowsForAsync(orderId);
        var row = Assert.Single(rows);
        Assert.Equal("order.cancelled", row.Type);

        // ADR-0005 replay: cancelling again is idempotent success — no second event.
        using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IOrderService>().CancelMineAsync(userId, orderId);

        Assert.Single(await RowsForAsync(orderId));
    }

    [Fact]
    public async Task AdminTransitions_EmitCatalogEvents_WithPaidPayloadFromPayment()
    {
        var (_, orderId, _) = await SeedPaidOrderAsync("txn-42");

        using (var scope = NewScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            await orders.UpdateStatusAdminAsync(orderId, OrderStatus.Paid);      // replay: no row
            await orders.UpdateStatusAdminAsync(orderId, OrderStatus.Processing); // no catalog event
            await orders.UpdateStatusAdminAsync(orderId, OrderStatus.Shipped);
            await orders.UpdateStatusAdminAsync(orderId, OrderStatus.Delivered);
        }

        var rows = await RowsForAsync(orderId);
        Assert.Equal(2, rows.Count); // shipped + delivered; Paid replay and Processing emit nothing
        Assert.Contains("order.shipped", rows.Select(r => r.Type));
        Assert.Contains("order.delivered", rows.Select(r => r.Type));
        Assert.All(rows, r => Assert.Equal(OutboxStatus.Pending, r.Status));
    }

    [Fact]
    public async Task PaidTransition_PayloadCarriesTotalAndTransactionId()
    {
        var (userId, orderId) = await SeedPendingOrderAsync();
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Payments.Add(new Payment { OrderId = orderId, Amount = 25m, Status = PaymentStatus.Succeeded, TransactionId = "txn-77" });
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<IOrderService>()
                .UpdateStatusAdminAsync(orderId, OrderStatus.Paid);
        }

        var row = Assert.Single(await RowsForAsync(orderId));
        Assert.Equal("order.paid", row.Type);
        using var doc = System.Text.Json.JsonDocument.Parse(row.Payload);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("txn-77", data.GetProperty("transactionId").GetString());
        Assert.Equal(25m, data.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(userId, doc.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task RejectedTransition_EmitsNoEvent()
    {
        var (_, orderId) = await SeedPendingOrderAsync();

        using var scope = NewScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        await Assert.ThrowsAsync<InvalidOrderStateException>(
            async () => await orders.UpdateStatusAdminAsync(orderId, OrderStatus.Delivered)); // Pending→Delivered illegal

        Assert.Empty(await RowsForAsync(orderId));
    }

    [Fact]
    public async Task CancelOfPaidOrder_RestoresStockOnce_ReplayDoesNotDoubleRestore()
    {
        // B1/M10 intersection: the conditional-transition loser sees a replay and
        // must apply NO side effects — neither the stock restore nor a second event.
        Guid productId;
        Guid userId;
        Guid orderId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
            var tag = Guid.NewGuid().ToString("N");
            var user = new User
            {
                Email = $"restore_{tag}@test.local", PasswordHash = "x",
                FirstName = "R", LastName = "T", Role = UserRole.Customer,
            };
            var product = new Product
            {
                Name = $"restore {tag}", Slug = $"restore-{tag}", SKU = $"RES-{tag}",
                CategoryId = categoryId, Price = 5m, Status = ProductStatus.Active,
                Inventory = new Inventory { Quantity = 2 }, // 3 units were consumed by the paid order
            };
            product.Inventory.ProductId = product.Id;
            var order = new Order
            {
                UserId = user.Id,
                OrderNumber = Order.GenerateOrderNumber(),
                Status = OrderStatus.Paid,
                Subtotal = 15m, ShippingFee = 0m, TotalAmount = 15m,
                ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
                {
                    FullName = "R T", Phone = "1", Address = "a", City = "c",
                },
            };
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id, ProductId = product.Id,
                ProductName = product.Name, SKU = product.SKU,
                UnitPrice = 5m, Quantity = 3, Subtotal = 15m,
            });
            db.Users.Add(user);
            db.Products.Add(product);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            productId = product.Id;
            userId = user.Id;
            orderId = order.Id;
        }

        using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IOrderService>().CancelMineAsync(userId, orderId);

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            Assert.Equal(5, (await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId)).Quantity);
        }

        // Replay the cancel: stock untouched, no duplicate event.
        using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IOrderService>().CancelMineAsync(userId, orderId);

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            Assert.Equal(5, (await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == productId)).Quantity);
        }
        Assert.Single(await RowsForAsync(orderId));
    }

    [Fact]
    public async Task ProductWrites_EmitCatalogEvents()
    {
        Guid productId;
        Guid categoryId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            categoryId = await db.Categories.AsNoTracking().Select(c => c.Id).FirstAsync();
        }

        using (var scope = NewScope())
        {
            var products = scope.ServiceProvider.GetRequiredService<IProductService>();
            var tag = Guid.NewGuid().ToString("N");
            var created = await products.CreateAsync(new CreateProductRequest(
                "Outbox widget", null, $"OBX-{tag}", categoryId, null, "Acme",
                19.99m, ProductStatus.Active, null, 5));
            productId = created.Id;

            await products.UpdateAsync(productId, new UpdateProductRequest(
                "Outbox widget v2", null, $"OBX-{tag}", categoryId, null, "Acme",
                24.99m, ProductStatus.Inactive, null));

            // Failed write must not emit: duplicate SKU throws before any save.
            await Assert.ThrowsAsync<DomainValidationException>(async () => await products.CreateAsync(
                new CreateProductRequest("dup", null, $"OBX-{tag}", categoryId, null, "Acme", 1m,
                    ProductStatus.Active, null, 1)));
        }

        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var rows = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.AggregateId == productId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Contains("product.created", rows.Select(r => r.Type));
            Assert.Contains("product.updated", rows.Select(r => r.Type));

            var updated = rows.Single(r => r.Type == "product.updated");
            using var doc = System.Text.Json.JsonDocument.Parse(updated.Payload);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(24.99m, data.GetProperty("price").GetDecimal());
            Assert.Equal("Inactive", data.GetProperty("status").GetString());
        }
    }
}
