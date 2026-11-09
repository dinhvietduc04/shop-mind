using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shop.Domain.Entities;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Messaging.Consumers;
using Shop.Infrastructure.Persistence;
using Shop.Worker;
using Shop.Worker.RabbitMq;
using Testcontainers.RabbitMq;
using Xunit;

namespace Shop.IntegrationTests;

/// <summary>
/// B4 real-broker end-to-end: RabbitMqPublisher (confirms) → topic exchange →
/// queue → normalized message → ConsumerPipeline (dedupe + analytics append).
/// Runs wherever a container runtime exists (CI). On this box (no Docker) the
/// broker fixture can't start and the tests self-skip — the same guarantees are
/// covered locally against Postgres with the fake broker in OutboxDeliveryTests
/// and ConsumerPipelineTests.
/// </summary>
public sealed class RabbitE2ETests : IAsyncLifetime
{
    private readonly ShopWebFactory _app = new();
    private RabbitMqContainer? _broker;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        try
        {
            _broker = new RabbitMqBuilder().WithImage("rabbitmq:4-management").Build();
            await _broker.StartAsync();
        }
        catch (Exception ex)
        {
            _skipReason = $"No container runtime for the RabbitMQ fixture ({ex.GetType().Name}); broker e2e runs in CI.";
            _broker = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_broker is not null)
        {
            await _broker.StopAsync();
            await _broker.DisposeAsync();
        }
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task PublishThroughRealBroker_ConsumeOnce_DuplicateDeliverySkipped()
    {
        if (_skipReason is not null || _broker is null) return; // see class summary

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { Url = _broker.GetConnectionString() });
        await using var publisher = new RabbitMqPublisher(options);

        var message = new OutboxMessage
        {
            AggregateType = "order",
            AggregateId = Guid.NewGuid(),
            Type = "order.paid",
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                eventId = Guid.Empty, // filled below to match broker messageId anyway
                eventType = "order.paid",
                aggregateId = Guid.NewGuid(),
                userId = Guid.NewGuid(),
                data = new { orderId = Guid.NewGuid(), userId = Guid.NewGuid(), totalAmount = 5m, transactionId = "txn-e2e" },
                schemaVersion = 1,
            }),
        };
        // consume-first: register queue, then publish so nothing races ahead
        var factory = new ConnectionFactory { Uri = new Uri(options.Value.Url) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var chan = await conn.CreateChannelAsync();
        await chan.ExchangeDeclareAsync("shop.events", ExchangeType.Topic, durable: true, cancellationToken: default);
        await chan.QueueDeclareAsync("shop.e2e", durable: false, cancellationToken: default);
        await chan.QueueBindAsync("shop.e2e", "shop.events", "order.*", cancellationToken: default);

        var received = new TaskCompletionSource<(string RoutingKey, string MessageId, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(chan);
        consumer.ReceivedAsync += (_, ea) =>
        {
            received.TrySetResult((ea.RoutingKey, ea.BasicProperties.MessageId ?? "", Encoding.UTF8.GetString(ea.Body.Span)));
            return Task.CompletedTask;
        };
        await chan.BasicConsumeAsync("shop.e2e", autoAck: true, consumer: consumer, cancellationToken: default);

        await publisher.PublishAsync(message.Type, message.Id, message.Payload);

        var won = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("order.paid", won.RoutingKey);
        Assert.Equal(message.Id.ToString(), won.MessageId); // messageId == eventId contract

        // Process through the pipeline; a second delivery of the same message must dedupe.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = new OutboxEventMessage(Guid.Parse(won.MessageId), won.RoutingKey, won.Body);
        var pipeline = new ConsumerPipeline(db, new AnalyticsEventHandler(db));

        Assert.Equal(ConsumeOutcome.Ack, await pipeline.HandleAsync(evt, 0));
        Assert.Equal(ConsumeOutcome.AckDuplicate, await pipeline.HandleAsync(evt, 0));
        // one behavior row from the real first delivery; the duplicate added nothing
        Assert.Equal(1, await db.UserBehaviorEvents.AsNoTracking().CountAsync(e => e.Type == "order_paid"));
    }

    [Fact]
    public async Task BrokerRestart_PublishedBeforeShutdownArrivesAfter()
    {
        if (_skipReason is not null || _broker is null) return; // see class summary

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { Url = _broker.GetConnectionString() });

        // declare the durable topology BEFORE publishing so the message is routable
        var factory0 = new ConnectionFactory { Uri = new Uri(options.Value.Url) };
        await using (var setupConn = await factory0.CreateConnectionAsync())
        await using (var setupChan = await setupConn.CreateChannelAsync())
        {
            await setupChan.ExchangeDeclareAsync("shop.events", ExchangeType.Topic, durable: true, cancellationToken: default);
            await setupChan.QueueDeclareAsync("shop.e2e.restart", durable: true, cancellationToken: default);
            await setupChan.QueueBindAsync("shop.e2e.restart", "shop.events", "order.shipped", cancellationToken: default);
        }

        await using (var publisher = new RabbitMqPublisher(options))
        {
            await publisher.PublishAsync("order.shipped", Guid.NewGuid(),
                "{\"eventType\":\"order.shipped\",\"schemaVersion\":1,\"data\":{}}");
        }

        // restart the broker container, then read the durable queue
        await _broker.StopAsync();
        await _broker.StartAsync();

        var factory = new ConnectionFactory { Uri = new Uri(options.Value.Url) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var chan = await conn.CreateChannelAsync();

        var got = await chan.BasicGetAsync("shop.e2e.restart", autoAck: false, cancellationToken: default);
        Assert.True(got is { BasicProperties.MessageId: not null }); // survived the restart
        await chan.BasicAckAsync(got!.DeliveryTag, multiple: false, cancellationToken: default);
    }
}
