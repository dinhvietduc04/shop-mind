using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Messaging.Consumers;

/// <summary>
/// B4 analytics consumer v1 (M11): order.* events append UserBehaviorEvents
/// rows for ops analytics. Collected, not personalization (M18 backlog).
/// </summary>
public sealed class AnalyticsEventHandler(AppDbContext db) : IEventConsumer
{
    public string Name => "analytics";

    public async Task ConsumeAsync(OutboxEventMessage message, CancellationToken ct = default)
    {
        // v1 scope (M11): order.* events become behavior rows; other types are
        // acknowledged with no row (product.viewed collection arrives with M18).
        if (!message.EventType.StartsWith("order.", System.StringComparison.Ordinal))
            return;

        using var doc = System.Text.Json.JsonDocument.Parse(message.PayloadJson);
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var d) ? d : root;

        var row = new Shop.Domain.Entities.UserBehaviorEvent
        {
            Type = message.EventType.Replace('.', '_'),
            UserId = data.TryGetProperty("userId", out var uid) && uid.ValueKind == System.Text.Json.JsonValueKind.String
                ? uid.GetGuid()
                : null,
            MetadataJson = data.GetRawText(),
        };
        db.UserBehaviorEvents.Add(row);
        await db.SaveChangesAsync(ct); // joins the pipeline's transaction
    }
}
