using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Interfaces.Events;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// B4: the delivery engine — atomic SKIP-LOCKED claiming, at-least-once publish
/// with Attempts accounting, broker-down keeps rows Pending, exhausted attempts
/// land Failed (outbox-side DLQ marker), and a crashed publisher's claimed-but
/// unacked rows get safely re-published. Postgres is the testable core; the
/// broker leg is a fake here (real RabbitMQ e2e runs in CI).
/// Seam: OutboxClaimService + OutboxPublisher over the disposable Postgres.
/// </summary>
public class OutboxDeliveryTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private sealed class RecordingBroker : IBrokerPublisher
    {
        public List<(string RoutingKey, Guid MessageId)> Published { get; } = [];
        /// <summary>Return true to simulate a broker fault for that message.</summary>
        public Func<Guid, bool>? Fail { get; set; }

        public Task PublishAsync(string routingKey, Guid messageId, string payloadJson, CancellationToken ct = default)
        {
            if (Fail?.Invoke(messageId) == true)
                throw new InvalidOperationException("broker fault (fake)");
            Published.Add((routingKey, messageId));
            return Task.CompletedTask;
        }
    }

    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    /// <summary>One class = one disposable DB, tests serialize — start each test from zero.</summary>
    private async Task CleanSlateAsync() => await NewDb().OutboxMessages.ExecuteDeleteAsync();

    private async Task<List<OutboxMessage>> SeedPendingAsync(int count)
    {
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, count).Select(i => new OutboxMessage
        {
            AggregateType = "order",
            AggregateId = Guid.NewGuid(),
            Type = "order.created",
            Payload = "{\"schemaVersion\":1}",
            OccurredAt = now.AddSeconds(i), // deterministic oldest-first order
            Status = OutboxStatus.Pending,
        }).ToList();
        var db = NewDb();
        db.OutboxMessages.AddRange(rows);
        await db.SaveChangesAsync();
        return rows;
    }

    private async Task<Dictionary<Guid, OutboxMessage>> ReloadAsync(IEnumerable<OutboxMessage> rows)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        return (await NewDb().OutboxMessages.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync())
            .ToDictionary(m => m.Id);
    }

    private (OutboxPublisher Publisher, RecordingBroker Broker) NewPublisher(int maxAttempts = 3)
    {
        var broker = new RecordingBroker();
        var db = NewDb();
        var publisher = new OutboxPublisher(db, new OutboxClaimService(db), broker,
            new OutboxPublisherOptions { BatchSize = 50, MaxAttempts = maxAttempts });
        return (publisher, broker);
    }

    [Fact]
    public async Task ClaimBatch_TakesOldestPendingFirst_AndCountsAttempts()
    {
        await CleanSlateAsync();
        var rows = await SeedPendingAsync(3);
        var claim = new OutboxClaimService(NewDb());

        var first = await claim.ClaimBatchAsync(2);

        Assert.Equal(new[] { rows[0].Id, rows[1].Id }.OrderBy(x => x), first.Select(m => m.Id).OrderBy(x => x));
        Assert.All(first, m => Assert.Equal(1, m.Attempts)); // Attempts++ at claim

        // A bare claim reserves only while its statement runs (a real round holds
        // the locks through publish+mark). Two sequential bare claims therefore
        // legitimately re-take the oldest rows, counting attempts again.
        var second = await claim.ClaimBatchAsync(2);
        Assert.Equal(first.Select(m => m.Id).OrderBy(x => x), second.Select(m => m.Id).OrderBy(x => x));
        Assert.All(second, m => Assert.Equal(2, m.Attempts));

        var reloaded = await ReloadAsync(rows);
        Assert.Equal(2, reloaded[rows[0].Id].Attempts);
        Assert.Equal(2, reloaded[rows[1].Id].Attempts);
        Assert.Equal(0, reloaded[rows[2].Id].Attempts); // third row never in a 2-batch
        Assert.All(rows, r => Assert.Equal(OutboxStatus.Pending, reloaded[r.Id].Status));
    }

    [Fact]
    public async Task ClaimBatch_SkipsSentAndFailedRows()
    {
        await CleanSlateAsync();
        var pending = await SeedPendingAsync(1);
        var db = NewDb();
        db.OutboxMessages.AddRange(
            new OutboxMessage
            {
                AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.paid",
                Payload = "{}", OccurredAt = DateTime.UtcNow.AddMinutes(-5),
                Status = OutboxStatus.Sent, PublishedAt = DateTime.UtcNow,
            },
            new OutboxMessage
            {
                AggregateType = "order", AggregateId = Guid.NewGuid(), Type = "order.paid",
                Payload = "{}", OccurredAt = DateTime.UtcNow.AddMinutes(-5), Status = OutboxStatus.Failed,
            });
        await db.SaveChangesAsync();

        var claimed = await new OutboxClaimService(NewDb()).ClaimBatchAsync(10);

        Assert.Equal(pending.Select(p => p.Id), claimed.Select(c => c.Id));
    }

    [Fact]
    public async Task ConcurrentRounds_NeverDoublePublishTheSameRow()
    {
        // Round-scoped transaction + SKIP LOCKED: whether the three publishers
        // overlap (locks exclude) or serialize (Sent rows skipped), every row is
        // claimed, published and marked exactly once across the fleet.
        await CleanSlateAsync();
        await SeedPendingAsync(10);

        var brokers = new List<RecordingBroker>();
        var rounds = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            var broker = new RecordingBroker();
            var db = NewDb();
            var publisher = new OutboxPublisher(db, new OutboxClaimService(db), broker,
                new OutboxPublisherOptions { BatchSize = 4, MaxAttempts = 3 });
            lock (brokers) { brokers.Add(broker); }
            return await publisher.RunOnceAsync();
        }));

        Assert.Equal(10, rounds.Sum(r => r.Sent));
        var published = brokers.SelectMany(b => b.Published).Select(p => p.MessageId).ToList();
        Assert.Equal(10, published.Count);
        Assert.Equal(10, published.Distinct().Count());

        var claim = new OutboxClaimService(NewDb());
        Assert.Empty(await claim.ClaimBatchAsync(50)); // nothing left Pending
    }

    [Fact]
    public async Task HappyRound_MarksSent_AndPublishesWithEventIdAsMessageId()
    {
        await CleanSlateAsync();
        var rows = await SeedPendingAsync(2);
        var (publisher, broker) = NewPublisher();

        var round = await publisher.RunOnceAsync();

        Assert.Equal(2, round.Sent);
        Assert.False(round.BrokerFaulted);
        foreach (var row in rows)
            Assert.Contains((row.Type, row.Id), broker.Published); // messageId == outbox row id

        var reloaded = await ReloadAsync(rows);
        Assert.All(rows, r =>
        {
            Assert.Equal(OutboxStatus.Sent, reloaded[r.Id].Status);
            Assert.NotNull(reloaded[r.Id].PublishedAt);
        });
    }

    [Fact]
    public async Task BrokerDown_KeepsRowsPendingUntilAttemptsAreExhausted_ThenFailed()
    {
        await CleanSlateAsync();
        var rows = await SeedPendingAsync(2);
        var db = NewDb();
        var failing = new OutboxPublisher(db, new OutboxClaimService(db),
            new RecordingBroker { Fail = _ => true },
            new OutboxPublisherOptions { BatchSize = 50, MaxAttempts = 3 });

        var r1 = await failing.RunOnceAsync();
        Assert.Equal(2, r1.Claimed);
        Assert.Equal(0, r1.Sent);
        Assert.True(r1.BrokerFaulted);
        var reloaded = await ReloadAsync(rows);
        Assert.All(rows, r =>
        {
            Assert.Equal(OutboxStatus.Pending, reloaded[r.Id].Status);
            Assert.Equal(1, reloaded[r.Id].Attempts);
        });

        await failing.RunOnceAsync(); // attempts 2
        var r3 = await failing.RunOnceAsync(); // attempts 3 → exhausted

        Assert.Equal(2, r3.Failed);
        reloaded = await ReloadAsync(rows);
        Assert.All(rows, r => Assert.Equal(OutboxStatus.Failed, reloaded[r.Id].Status));

        var r4 = await failing.RunOnceAsync();
        Assert.Equal(0, r4.Claimed); // Failed rows are never re-claimed
    }

    [Fact]
    public async Task CrashedAfterDelivery_RowStaysPending_NextRoundRepublishes_AtLeastOnce()
    {
        // Publisher "crashes" after the broker accepted the message but before
        // MarkSent: the row is still Pending, so the next round publishes again —
        // the broker sees the SAME messageId twice. Delivery is at-least-once by
        // design; consumer-side dedupe (ProcessedEvents) makes processing once.
        await CleanSlateAsync();
        var rows = await SeedPendingAsync(1);
        var deliveries = new List<Guid>();
        var broker = new RecordingBroker
        {
            Fail = id =>
            {
                deliveries.Add(id); // message truly went out...
                return deliveries.Count(d => d == id) == 1; // ...but the round faults: first delivery is "lost on the floor"
            },
        };
        var db = NewDb();
        var publisher = new OutboxPublisher(db, new OutboxClaimService(db), broker,
            new OutboxPublisherOptions { BatchSize = 10, MaxAttempts = 5 });

        var lost = await publisher.RunOnceAsync();
        Assert.Equal(0, lost.Sent);
        Assert.Equal(OutboxStatus.Pending, (await ReloadAsync(rows))[rows[0].Id].Status);

        var recovered = await publisher.RunOnceAsync();
        Assert.Equal(1, recovered.Sent);
        Assert.Equal(OutboxStatus.Sent, (await ReloadAsync(rows))[rows[0].Id].Status);

        Assert.Equal(2, deliveries.Count); // at-least-once: two broker deliveries, one row, marked Sent once
        Assert.Single(broker.Published);
    }
}
