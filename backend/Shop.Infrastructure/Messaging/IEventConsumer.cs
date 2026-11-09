namespace Shop.Infrastructure.Messaging;

/// <summary>B4 consumer contract: named, side-effecting handler of one event.</summary>
public interface IEventConsumer
{
    /// <summary>Dedupe key namespace — one ledger slot per (EventId, Consumer).</summary>
    string Name { get; }

    Task ConsumeAsync(OutboxEventMessage message, CancellationToken ct = default);
}
