using Microsoft.EntityFrameworkCore;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Messaging;

/// <summary>
/// B4 (M11 retention): deletes outbox rows already published and older than a
/// cutoff — Pending/Failed are owed and never touched.
/// </summary>
public sealed class OutboxRetentionService(AppDbContext db)
{
    public async Task<int> PurgeSentAsync(DateTime cutoffUtc, CancellationToken ct = default)
        => await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Sent
                && (m.PublishedAt ?? m.OccurredAt) < cutoffUtc)
            .ExecuteDeleteAsync(ct);
}
