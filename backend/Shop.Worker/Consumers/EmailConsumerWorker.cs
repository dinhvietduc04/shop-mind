using Microsoft.Extensions.Options;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Messaging.Consumers;
using Shop.Worker.RabbitMq;

namespace Shop.Worker.Consumers;

/// <summary>B4 (M11): shop.email queue → EmailEventHandler (structured-log stub).</summary>
public sealed class EmailConsumerWorker(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> rabbit, ILogger<EmailConsumerWorker> log)
    : EventConsumerWorkerBase(scopes, rabbit, log)
{
    protected override string QueueName => "shop.email";
    // order.* + the human-facing payment outcomes only — NOT payment.webhook-received
    // (that is the internal intake key the PaymentWebhookQueueWorker eats).
    protected override IReadOnlyList<string> RoutingKeys { get; } = ["order.*", "payment.failed", "payment.anomaly"];
    protected override IEventConsumer CreateConsumer(IServiceScope scope) => new EmailEventHandler(
        scope.ServiceProvider.GetRequiredService<ILogger<EmailEventHandler>>());
}
