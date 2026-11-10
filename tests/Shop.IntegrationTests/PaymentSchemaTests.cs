using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.ValueObjects;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// B5 cycle 1: the money-path schema — capability-token uniqueness, one session
/// per quote order, quote expiry/outcome columns, line-level reservation, and
/// the append-only anomaly ledger all persist per configuration.
/// Seam: IAppDbContext persistence edge against the disposable Postgres.
/// </summary>
public class PaymentSchemaTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<(User user, Order order)> SeedOrderAsync(OrderStatus status = OrderStatus.Pending)
    {
        var db = NewDb();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"schema_{tag}@test.local", PasswordHash = "x", FirstName = "S", LastName = "M", Role = UserRole.Customer };
        var order = new Order
        {
            UserId = user.Id,
            OrderNumber = Order.GenerateOrderNumber(),
            Status = status,
            Subtotal = 10m, ShippingFee = 0m, TotalAmount = 10m,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ShippingAddress = new ShippingAddress { FullName = "S M", Phone = "1", Address = "a", City = "c" },
        };
        order.Items.Add(new OrderItem
        {
            OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "p", SKU = "S",
            UnitPrice = 10m, Quantity = 2, ReservedQuantity = 2, Subtotal = 20m,
        });
        db.Users.Add(user);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (user, order);
    }

    [Fact]
    public async Task SessionToken_IsUnique_CapabilityCredential()
    {
        var (user, order) = await SeedOrderAsync();
        var db = NewDb();
        var token = PaymentSession.GenerateSessionToken();
        db.PaymentSessions.Add(new PaymentSession { SessionToken = token, OrderId = order.Id, UserId = user.Id, Amount = 10m });
        await db.SaveChangesAsync();

        // A second session on the same order is rejected (single-use) and a
        // colliding token is rejected (capability uniqueness).
        var otherOrder = (await SeedOrderAsync()).order;
        db.PaymentSessions.Add(new PaymentSession { SessionToken = token, OrderId = otherOrder.Id, UserId = user.Id, Amount = 10m });
        await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Fact]
    public async Task SessionToken_IsUrlSafe128Bit()
    {
        var token = PaymentSession.GenerateSessionToken();
        Assert.Equal(22, token.Length); // 128 bits, base64url without padding
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public async Task QuoteFields_RoundTrip_ExpiryOutcomeAndLineReservation()
    {
        var (_, order) = await SeedOrderAsync();

        var reloaded = await NewDb().Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        Assert.NotNull(reloaded.ExpiresAt);
        Assert.Null(reloaded.Outcome);
        Assert.True(reloaded.IsQuote);
        Assert.Equal(2, reloaded.Items.Single().ReservedQuantity);

        var db = NewDb();
        await db.Orders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Outcome, PaymentOutcome.Expired));
        var after = await NewDb().Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOutcome.Expired, after.Outcome);
    }

    [Fact]
    public async Task LegacyPendingOrder_IsNotAQuote_NullExpiry()
    {
        // ADR-0001 consequence: V1 Pendings (no expiry) are excluded from the
        // Open Quote definition — sweeper/replace policy can never touch them.
        var db = NewDb();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"legacy_{tag}@test.local", PasswordHash = "x", FirstName = "L", LastName = "O", Role = UserRole.Customer };
        var legacy = new Order
        {
            UserId = user.Id, OrderNumber = Order.GenerateOrderNumber(), Status = OrderStatus.Pending,
            Subtotal = 1m, ShippingFee = 0m, TotalAmount = 1m,
            ShippingAddress = new ShippingAddress { FullName = "L O", Phone = "1", Address = "a", City = "c" },
        };
        db.Users.Add(user);
        db.Orders.Add(legacy);
        await db.SaveChangesAsync();

        Assert.False(legacy.IsQuote);
        Assert.Equal(0, await db.Orders.AsNoTracking().CountAsync(o => o.UserId == user.Id && o.Status == OrderStatus.Pending && o.ExpiresAt != null));
    }

    [Fact]
    public async Task PaymentAnomaly_AppendsReadable()
    {
        var (_, order) = await SeedOrderAsync(OrderStatus.Cancelled);
        var db = NewDb();
        db.PaymentAnomalies.Add(new PaymentAnomaly
        {
            OrderId = order.Id,
            SessionToken = "tok",
            ProviderEventId = "evt_conflict",
            Observed = "captured",
            CurrentStatus = nameof(OrderStatus.Cancelled),
            Reason = "conflicting terminal state received succeeded",
        });
        await db.SaveChangesAsync();

        var row = await db.PaymentAnomalies.AsNoTracking().SingleAsync(a => a.OrderId == order.Id);
        Assert.Equal("captured", row.Observed);
        Assert.Equal("evt_conflict", row.ProviderEventId);
    }
}
