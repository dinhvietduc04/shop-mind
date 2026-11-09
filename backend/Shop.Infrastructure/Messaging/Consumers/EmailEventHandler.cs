using Microsoft.Extensions.Logging;

namespace Shop.Infrastructure.Messaging.Consumers;

/// <summary>
/// B4 email consumer v1 = structured log only (M11: "real SMTP deferred").
/// </summary>
public sealed class EmailEventHandler(ILogger<EmailEventHandler> log) : IEventConsumer
{
    public string Name => "email";

    public Task ConsumeAsync(OutboxEventMessage message, CancellationToken ct = default)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(message.PayloadJson);
        var userId = doc.RootElement.TryGetProperty("userId", out var u) && u.ValueKind != System.Text.Json.JsonValueKind.Null
            ? u.GetString()
            : "n/a";
        log.LogInformation("[Email] {EventType} -> user {UserId}", message.EventType, userId);
        return Task.CompletedTask;
    }
}
