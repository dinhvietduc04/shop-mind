using Microsoft.Extensions.Options;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Messaging.Consumers;
using Shop.Infrastructure.Persistence;
using Shop.Worker.RabbitMq;

namespace Shop.Worker.Consumers;

/// <summary>B4 (M11): shop.analytics queue → AnalyticsEventHandler (behavior append).</summary>
public sealed class AnalyticsConsumerWorker(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> rabbit, ILogger<AnalyticsConsumerWorker> log)
    : EventConsumerWorkerBase(scopes, rabbit, log)
{
    protected override string QueueName => "shop.analytics";
    protected override IReadOnlyList<string> RoutingKeys { get; } = ["order.*", "product.*"];
    protected override IEventConsumer CreateConsumer(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AnalyticsEventHandler>();
}
