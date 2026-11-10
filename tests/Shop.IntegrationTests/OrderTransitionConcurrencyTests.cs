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
/// B1: order status transitions are conditional updates
/// (UPDATE orders SET status=@new WHERE id=@id AND status=@expected), so racing
/// transitions serialize — exactly one wins, the loser gets a typed outcome, never a
/// silent overwrite. (The full ADR-0005 outcome matrix — mainline-downstream and
/// PaymentAnomaly — lands with B5's webhook path; these tests pin the atomic base.)
/// Seam: IOrderService public boundary.
/// </summary>
public class OrderTransitionConcurrencyTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private IOrderService OrderService() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<IOrderService>();

    private async Task<(Guid userId, Guid orderId)> SeedPendingOrderAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Email = $"transition_{tag}@test.local",
            PasswordHash = "x",
            FirstName = "Trans",
            LastName = "Test",
            Role = UserRole.Customer,
        };
        var order = new Order
        {
            UserId = user.Id,
            OrderNumber = Order.GenerateOrderNumber(),
            Status = OrderStatus.Pending,
            Subtotal = 10m,
            ShippingFee = 0m,
            TotalAmount = 10m,
            ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
            {
                FullName = "T T", Phone = "1", Address = "a", City = "c",
            },
        };
        db.Users.Add(user);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (user.Id, order.Id);
    }

    [Fact]
    public async Task CancelVsMarkPaid_Concurrent_ExactlyOneWinsAndLoserIsTyped()
    {
        // Repeat the race: read-then-write implementations let the loser's read
        // sometimes land after the winner commits (accidentally "correct"); a
        // conditional update is deterministic. Any overlap failure must be typed,
        // never a raw infrastructure exception or silent double-success.
        for (var round = 0; round < 5; round++)
        {
            var (userId, orderId) = await SeedPendingOrderAsync();

            Task<OrderDto> cancel = OrderService().CancelMineAsync(userId, orderId);
            Task<OrderDto> markPaid = OrderService().UpdateStatusAdminAsync(orderId, OrderStatus.Paid);
            try
            {
                await Task.WhenAll(cancel, markPaid);
            }
            catch
            {
                // one of the two raced transitions is expected to fail; ensure it's the
                // documented failure mode, not an infrastructure exception leaking through
                var failures = new List<Exception>();
                await Observe(cancel, failures);
                await Observe(markPaid, failures);
                Assert.Single(failures);
                Assert.IsType<InvalidOrderStateException>(failures[0]);
            }

            // Exactly one outcome recorded; both paths returned the same final status.
            var winner = cancel.IsCompletedSuccessfully ? cancel.Result : markPaid.Result;
            var final = await OrderService().GetAdminByIdAsync(orderId);
            Assert.Equal(final.Status, winner.Status);
            Assert.Contains(final.Status, new[] { nameof(OrderStatus.Cancelled), nameof(OrderStatus.Paid) });
        }
    }

    private static async Task Observe(Task<OrderDto> task, List<Exception> failures)
    {
        try { await task; }
        catch (Exception ex) { failures.Add(ex); }
    }

    [Fact]
    public async Task MarkPaid_WhenAlreadyPaid_IsIdempotentSuccess()
    {
        // ADR-0005 row 1: re-delivered transition whose target is already the
        // current state replays as success, not an error.
        var (_, orderId) = await SeedPendingOrderAsync();
        await OrderService().UpdateStatusAdminAsync(orderId, OrderStatus.Paid);

        var replay = await OrderService().UpdateStatusAdminAsync(orderId, OrderStatus.Paid);

        Assert.Equal(nameof(OrderStatus.Paid), replay.Status);
    }

    [Fact]
    public async Task Cancel_WhenAlreadyCancelled_IsIdempotentSuccess()
    {
        var (userId, orderId) = await SeedPendingOrderAsync();
        await OrderService().CancelMineAsync(userId, orderId);

        var replay = await OrderService().CancelMineAsync(userId, orderId);

        Assert.Equal(nameof(OrderStatus.Cancelled), replay.Status);
    }
}
