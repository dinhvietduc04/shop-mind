using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Domain.Entities;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Retention;

namespace Shop.IntegrationTests;

/// <summary>
/// B4: the retention sweeper owns collected analytics data. Behavior events are
/// ops data with a 12-month horizon; the purge must never touch recent rows.
/// </summary>
public class RetentionSweeperTests(ShopWebFactory factory) : IClassFixture<ShopWebFactory>
{
    [Fact]
    public async Task Behavior_events_past_the_horizon_are_purged_and_recent_ones_kept()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expired = new UserBehaviorEvent
        {
            Type = "product_view",
            CreatedAt = DateTime.UtcNow.AddDays(-400),
        };
        var recent = new UserBehaviorEvent
        {
            Type = "product_view",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
        };
        db.UserBehaviorEvents.AddRange(expired, recent);
        await db.SaveChangesAsync();

        var removed = await new RetentionService(db)
            .PurgeBehaviorEventsAsync(DateTime.UtcNow.AddDays(-365));

        Assert.True(removed >= 1);
        Assert.False(await db.UserBehaviorEvents.AnyAsync(e => e.Id == expired.Id));
        Assert.True(await db.UserBehaviorEvents.AnyAsync(e => e.Id == recent.Id));
    }
}
