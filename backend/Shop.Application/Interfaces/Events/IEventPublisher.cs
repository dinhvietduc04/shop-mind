using Shop.Domain.Events;

namespace Shop.Application.Interfaces;

/// <summary>
/// M10: enqueues a catalog event as an outbox row on the CALLER's DbContext —
/// it commits or rolls back with the business transaction. Nothing publishes
/// here; that is the M11 worker's job.
/// </summary>
public interface IEventPublisher
{
    Task EnqueueAsync(IDomainEvent domainEvent, CancellationToken ct = default);
}
