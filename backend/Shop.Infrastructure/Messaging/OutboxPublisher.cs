using Shop.Application.Interfaces.Events;

namespace Shop.Infrastructure.Messaging;

/// <summary>
/// B4: one publishing round over the outbox, wrapped in ONE transaction:
/// claim (FOR UPDATE SKIP LOCKED, locks held) → publish (broker confirms) →
/// mark Sent/Failed → commit. Consequence: concurrent publishers never
/// double-publish the same row — claimed rows stay locked until the round's
/// outcome is recorded, and an un-caught crash rolls the round back (rows
/// revert to their pre-claim state, safe to retry; delivery stays at-least-once).
/// A per-row broker fault still COMMITS (Attempts++ persists, row stays
/// Pending); MaxAttempts exhaustion marks Failed — the outbox-side dead letter.
///
/// The Shop.Worker BackgroundService is a thin shell around RunOnceAsync with
/// the poll loop + broker-down exponential backoff.
/// </summary>
public sealed class OutboxPublisher(
    Shop.Infrastructure.Persistence.AppDbContext db,
    OutboxClaimService claim,
    IBrokerPublisher broker,
    OutboxPublisherOptions options)
{
    public async Task<OutboxPublishRound> RunOnceAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await claim.ClaimBatchAsync(options.BatchSize, ct);
        if (claimed.Count == 0)
            return new OutboxPublishRound(0, 0, 0, 0, BrokerFaulted: false);

        var sent = new List<Guid>();
        var exhausted = new List<Guid>();
        var brokerFaulted = false;

        foreach (var row in claimed)
        {
            try
            {
                await broker.PublishAsync(row.Type, row.Id, row.Payload, ct);
                sent.Add(row.Id);
            }
            catch (OperationCanceledException)
            {
                throw; // host shutdown: roll the round back, retry cleanly next time
            }
            catch (Exception)
            {
                brokerFaulted = true;
                if (row.Attempts >= options.MaxAttempts)
                    exhausted.Add(row.Id); // dead-letter; stays Pending otherwise
            }
        }

        await claim.MarkSentAsync(sent, ct);
        await claim.MarkFailedAsync(exhausted, ct);
        await tx.CommitAsync(ct); // a caught fault still persists its Attempts++

        return new OutboxPublishRound(
            claimed.Count, sent.Count, exhausted.Count,
            claimed.Count - sent.Count - exhausted.Count, brokerFaulted);
    }
}
