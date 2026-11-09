using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Messaging.Consumers;

namespace Shop.IntegrationTests;

/// <summary>
/// B4b consumer discipline at the DB edge: exactly-once processing via
/// (EventId, Consumer) dedupe INSIDE the same transaction as the consumer's
/// writes — a handler fault rolls the dedupe mark back so redelivery
/// reprocesses, and the third redelivery dead-letters instead of retrying.
/// Seam: ConsumerPipeline + ProcessedEvents/UserBehaviorEvents assertions.
/// </summary>
public class ConsumerPipelineTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static OutboxEventMessage Message(string type = "order.created", Guid? eventId = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventId = eventId ?? Guid.Empty,
            eventType = type,
            schemaVersion = 1,
            data = new { orderId = Guid.NewGuid(), userId = Guid.NewGuid(), totalAmount = 12.5m, itemCount = 2 },
        });
        return new OutboxEventMessage(eventId ?? Guid.NewGuid(), type, payload);
    }

    private sealed class CountingConsumer : IEventConsumer
    {
        public string Name { get; init; } = "test-consumer";
        public int Calls;
        public Func<OutboxEventMessage, Task> Body = _ => Task.CompletedTask;
        public Task ConsumeAsync(OutboxEventMessage message, CancellationToken ct = default)
        {
            Calls++;
            return Body(message);
        }
    }

    [Fact]
    public async Task FirstDelivery_ProcessesOnceAndMarksDeduped()
    {
        var db = NewDb();
        var consumer = new CountingConsumer();
        var pipeline = new ConsumerPipeline(db, consumer);
        var msg = Message();

        var outcome = await pipeline.HandleAsync(msg, redeliveryCount: 0);

        Assert.Equal(ConsumeOutcome.Ack, outcome);
        Assert.Equal(1, consumer.Calls);
        Assert.Equal(1, await db.ProcessedEvents.AsNoTracking().CountAsync(p => p.EventId == msg.EventId && p.Consumer == "test-consumer"));
    }

    [Fact]
    public async Task DuplicateDelivery_IsSkipped_ConsumerRunsExactlyOnce()
    {
        // M11 acceptance: publish the same eventId twice → consumer processes once.
        var db = NewDb();
        var consumer = new CountingConsumer();
        var pipeline = new ConsumerPipeline(db, consumer);
        var msg = Message();

        Assert.Equal(ConsumeOutcome.Ack, await pipeline.HandleAsync(msg, 0));
        var dup = await pipeline.HandleAsync(msg, 0);

        Assert.Equal(ConsumeOutcome.AckDuplicate, dup);
        Assert.Equal(1, consumer.Calls);
    }

    [Fact]
    public async Task DifferentConsumers_EachGetTheirOwnDedupeKey()
    {
        var db = NewDb();
        var msg = Message();

        Assert.Equal(ConsumeOutcome.Ack, await new ConsumerPipeline(db, new CountingConsumer { Name = "email" }).HandleAsync(msg, 0));
        var other = new CountingConsumer { Name = "analytics" };
        Assert.Equal(ConsumeOutcome.Ack, await new ConsumerPipeline(db, other).HandleAsync(msg, 0));

        Assert.Equal(1, other.Calls); // same event, different consumer — not deduped
        Assert.Equal(2, await db.ProcessedEvents.AsNoTracking().CountAsync(p => p.EventId == msg.EventId));
    }

    [Fact]
    public async Task HandlerFault_RollsBackDedupe_RedeliveryReprocesses()
    {
        var db = NewDb();
        var consumer = new CountingConsumer
        {
            Body = _ => throw new InvalidOperationException("transient failure"),
        };
        var pipeline = new ConsumerPipeline(db, consumer);
        var msg = Message();

        var first = await pipeline.HandleAsync(msg, redeliveryCount: 0);

        Assert.Equal(ConsumeOutcome.Requeue, first);
        Assert.Equal(0, await db.ProcessedEvents.AsNoTracking().CountAsync(p => p.EventId == msg.EventId)); // rolled back

        consumer.Body = _ => Task.CompletedTask; // recovered
        var second = await pipeline.HandleAsync(msg, redeliveryCount: 1);

        Assert.Equal(ConsumeOutcome.Ack, second);
        Assert.Equal(2, consumer.Calls); // tried twice; side effects land exactly once (dedupe row now present)
        Assert.Equal(1, await db.ProcessedEvents.AsNoTracking().CountAsync(p => p.EventId == msg.EventId));
    }

    [Fact]
    public async Task ThirdRedelivery_DeadLetters_WithoutInvokingConsumer()
    {
        // Poison message: after 3 broker redeliveries it goes to the DLQ — the
        // consumer is not hammered a fourth time, and no dedupe row is written.
        var db = NewDb();
        var consumer = new CountingConsumer
        {
            Body = _ => throw new InvalidOperationException("poison"),
        };
        var pipeline = new ConsumerPipeline(db, consumer);

        var outcome = await pipeline.HandleAsync(Message(), redeliveryCount: 3);

        Assert.Equal(ConsumeOutcome.DeadLetter, outcome);
        Assert.Equal(0, consumer.Calls);
    }

    [Fact]
    public async Task AnalyticsConsumer_AppendsBehaviorRow_DuplicateSkipped()
    {
        // M11 acceptance end-to-end (minus AMQP): the real analytics consumer
        // turns order.created into a UserBehaviorEvents row, exactly-once.
        var db = NewDb();
        var pipeline = new ConsumerPipeline(db, new AnalyticsEventHandler(db));
        var msg = Message("order.paid");

        Assert.Equal(ConsumeOutcome.Ack, await pipeline.HandleAsync(msg, 0));
        Assert.Equal(ConsumeOutcome.AckDuplicate, await pipeline.HandleAsync(msg, 0));

        var row = await db.UserBehaviorEvents.AsNoTracking().SingleAsync(e => e.Type == "order_paid");
        Assert.NotNull(row.UserId);
        using var meta = JsonDocument.Parse(row.MetadataJson);
        Assert.True(meta.RootElement.TryGetProperty("orderId", out _));
    }

    [Fact]
    public async Task RetentionPurge_DeletesOnlyOldSentRows()
    {
        // M11: outbox growth control — Sent rows past the retention horizon are
        // deleted; Pending/Failed are never touched (still owed work), fresh
        // Sent rows stay (audit window).
        var db = NewDb();
        var cutoff = DateTime.UtcNow.AddDays(-30); // production: KeepDays=30
        var oldSent = new OutboxMessage { AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.created", Payload = "{}", OccurredAt = cutoff.AddDays(-1), PublishedAt = cutoff.AddDays(-1), Status = OutboxStatus.Sent };
        var freshSent = new OutboxMessage { AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.created", Payload = "{}", OccurredAt = cutoff.AddDays(2), PublishedAt = cutoff.AddDays(2), Status = OutboxStatus.Sent };
        var oldPending = new OutboxMessage { AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.created", Payload = "{}", OccurredAt = cutoff.AddDays(-40), Status = OutboxStatus.Pending };
        var oldFailed = new OutboxMessage { AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.created", Payload = "{}", OccurredAt = cutoff.AddDays(-40), Status = OutboxStatus.Failed };
        db.OutboxMessages.AddRange(oldSent, freshSent, oldPending, oldFailed);
        await db.SaveChangesAsync();

        var removed = await new OutboxRetentionService(db).PurgeSentAsync(cutoff, ct: default);

        Assert.Equal(1, removed);
        Assert.Equal(
            new[] { freshSent.Id, oldPending.Id, oldFailed.Id }.OrderBy(x => x),
            (await db.OutboxMessages.AsNoTracking().Select(m => m.Id).ToArrayAsync()).OrderBy(x => x));
    }
}
