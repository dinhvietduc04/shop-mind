using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Messaging;

/// <summary>
/// B4: exactly-once processing discipline — the dedupe INSERT and the
/// consumer's writes share ONE transaction: a duplicate cannot sneak in
/// (unique (EventId, Consumer) rejects it), and a fault rolls the dedupe mark
/// back so the broker redelivery reprocesses cleanly. After MaxRedeliveries
/// the message goes to the DLQ instead of hammering the consumer.
/// </summary>
public sealed class ConsumerPipeline(AppDbContext db, IEventConsumer consumer)
{
    public const int MaxRedeliveries = 3;

    public async Task<ConsumeOutcome> HandleAsync(OutboxEventMessage message, int redeliveryCount, CancellationToken ct = default)
    {
        if (redeliveryCount >= MaxRedeliveries)
            return ConsumeOutcome.DeadLetter; // the AMQP layer moves it to shop.events.dlq; consumer untouched

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Cheap pre-check first: adding an entity whose composite key is
            // already tracked would throw before we ever hit the database.
            var seen = await db.ProcessedEvents.AsNoTracking()
                .AnyAsync(p => p.EventId == message.EventId && p.Consumer == consumer.Name, ct);
            if (seen)
            {
                await tx.RollbackAsync(ct);
                return ConsumeOutcome.AckDuplicate;
            }

            // Dedupe INSERT FIRST, in the same transaction as the side effects:
            // the unique (EventId, Consumer) key is the arbiter, so two racing
            // deliveries of the same event can never both process it.
            db.ProcessedEvents.Add(new Shop.Domain.Entities.ProcessedEvent
            {
                EventId = message.EventId,
                Consumer = consumer.Name,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Lost the race against a concurrent duplicate — same verdict.
            await tx.RollbackAsync(ct);
            db.ClearChangeTracker(); // the rejected dedupe row must not ride into the next message
            return ConsumeOutcome.AckDuplicate; // already consumed by this consumer
        }

        try
        {
            await consumer.ConsumeAsync(message, ct);
            await tx.CommitAsync(ct);
            return ConsumeOutcome.Ack;
        }
        catch (OperationCanceledException)
        {
            await tx.RollbackAsync(ct);
            db.ClearChangeTracker();
            throw;
        }
        catch (Exception)
        {
            // Fault: the dedupe mark rolls back with the consumer's writes, so
            // the broker redelivery reprocesses from scratch — exactly-once holds.
            await tx.RollbackAsync(ct);
            db.ClearChangeTracker();
            return ConsumeOutcome.Requeue;
        }
    }
}
