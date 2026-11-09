using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Common;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Events;

namespace Shop.IntegrationTests;

/// <summary>
/// M10: the outbox seam — EnqueueAsync writes a Pending row on the CALLER's
/// transaction, payload conforms to locked contract v1, rollback discards it
/// atomically with the business rows, correlation id rides along.
/// Seam: IEventPublisher + IAppDbContext.OutboxMessages.
/// </summary>
public class OutboxPublisherTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private readonly Guid UserId = Guid.NewGuid();
    private readonly Guid OrderId = Guid.NewGuid();

    private (IEventPublisher Publisher, IAppDbContext Db, IServiceScope Scope) Resolve()
    {
        var scope = factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IEventPublisher>(),
                scope.ServiceProvider.GetRequiredService<IAppDbContext>(),
                scope);
    }

    [Fact]
    public async Task Enqueue_AddsPendingOutboxRow_WithCatalogIdentity()
    {
        var (publisher, db, scope) = Resolve();
        using (scope)
        {
            await publisher.EnqueueAsync(new OrderPaidEvent(OrderId, UserId, 42.5m, "txn-1"));
            await db.SaveChangesAsync();

            var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == OrderId);
            Assert.Equal("order.paid", row.Type);
            Assert.Equal("order", row.AggregateType);
            Assert.Equal(OutboxStatus.Pending, row.Status);
            Assert.Equal(0, row.Attempts);
            Assert.Null(row.PublishedAt);
            Assert.True((DateTime.UtcNow - row.OccurredAt).TotalMinutes < 1);
        }
    }

    [Fact]
    public async Task Payload_ConformsToContractV1()
    {
        var (publisher, db, scope) = Resolve();
        using (scope)
        {
            await publisher.EnqueueAsync(new OrderPaidEvent(OrderId, UserId, 42.5m, "txn-1"));
            await db.SaveChangesAsync();

            var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == OrderId);
            using var doc = JsonDocument.Parse(row.Payload);
            var root = doc.RootElement;

            Assert.Equal(row.Id, root.GetProperty("eventId").GetGuid());
            Assert.Equal("order.paid", root.GetProperty("eventType").GetString());
            Assert.True(root.TryGetProperty("occurredAt", out _));
            Assert.Equal("order", root.GetProperty("aggregateType").GetString());
            Assert.Equal(OrderId, root.GetProperty("aggregateId").GetGuid());
            Assert.Equal(UserId, root.GetProperty("userId").GetGuid());
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

            var data = root.GetProperty("data");
            Assert.Equal(OrderId, data.GetProperty("orderId").GetGuid());
            Assert.Equal(42.5m, data.GetProperty("totalAmount").GetDecimal());
            Assert.Equal("txn-1", data.GetProperty("transactionId").GetString());
        }
    }

    [Fact]
    public async Task CorrelationId_RidesIntoPayload_WhenActive()
    {
        var (publisher, db, scope) = Resolve();
        using (scope)
        using (CorrelationContext.Begin("corr-123"))
        {
            await publisher.EnqueueAsync(new OrderCancelledEvent(OrderId, UserId, "owner request"));
            await db.SaveChangesAsync();

            var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == OrderId);
            using var doc = JsonDocument.Parse(row.Payload);
            Assert.Equal("corr-123", doc.RootElement.GetProperty("correlationId").GetString());
        }
    }

    [Fact]
    public async Task Rollback_DiscardsOutboxRow_Commit_KeepsIt()
    {
        // The outbox guarantee: event and business rows share one transaction.
        var (publisher, db, scope) = Resolve();
        using (scope)
        {
            await using (var tx = await db.BeginTransactionAsync())
            {
                await publisher.EnqueueAsync(new OrderCreatedEvent(OrderId, UserId, 10m, 1));
                await db.SaveChangesAsync();
                await tx.RollbackAsync();
            }
            Assert.Equal(0, await db.OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == OrderId));

            await using (var tx = await db.BeginTransactionAsync())
            {
                await publisher.EnqueueAsync(new OrderCreatedEvent(OrderId, UserId, 10m, 1));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            Assert.Equal(1, await db.OutboxMessages.AsNoTracking().CountAsync(m => m.AggregateId == OrderId));
        }
    }
}
