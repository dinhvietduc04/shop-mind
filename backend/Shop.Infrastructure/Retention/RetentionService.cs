using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Retention;

/// <summary>
/// B4 retention sweeper. Behavior events are ops data (M18 schema) and carry a
/// 12-month horizon. The AI conversation/message and tool-call slices named in
/// the plan activate with their tables in A5 (milestone 11) and A7 (milestone
/// 12); this service grows one purge method per owned table.
/// </summary>
public sealed class RetentionService(AppDbContext db)
{
    public Task<int> PurgeBehaviorEventsAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        db.UserBehaviorEvents
            .Where(e => e.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync(ct);
}
