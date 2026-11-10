using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Persistence;
using Shop.Worker.RabbitMq;

namespace Shop.Worker.Consumers;

/// <summary>
/// B4: one AMQP queue consumer as a BackgroundService. Manual acks, prefetch 10,
/// exactly-once processing via <see cref="ConsumerPipeline"/>, poison messages to
/// the DLQ after <see cref="ConsumerPipeline.MaxRedeliveries"/> broker
/// redeliveries. A dropped broker never crashes the host — reconnect, back off.
/// </summary>
public abstract class EventConsumerWorkerBase(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> rabbit, ILogger log) : BackgroundService
{
    protected abstract string QueueName { get; }
    protected abstract IReadOnlyList<string> RoutingKeys { get; }
    protected abstract IEventConsumer CreateConsumer(IServiceScope scope);

    private readonly ConnectionFactory factory = new()
    {
        Uri = new Uri(rabbit.Value.Url),
        ClientProvidedName = $"{rabbit.Value.ClientName}-{typeof(EventConsumerWorkerBase).Name}",
        AutomaticRecoveryEnabled = true,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectedAsync(stoppingToken);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Broker connection for {Queue} dropped; reconnecting in {Backoff}s", QueueName, backoff.TotalSeconds);
                try { await Task.Delay(backoff, stoppingToken); } catch (OperationCanceledException) { return; }
                backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
            }
        }
    }

    private async Task RunConnectedAsync(CancellationToken ct)
    {
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: ct);

        // Topology (idempotent declares): topic exchange, dead-letter exchange +
        // queue, this queue bound to its routing keys.
        await channel.ExchangeDeclareAsync(rabbit.Value.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(rabbit.Value.DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = rabbit.Value.DeadLetterExchange },
            cancellationToken: ct);
        foreach (var key in RoutingKeys)
            await channel.QueueBindAsync(QueueName, rabbit.Value.Exchange, key, cancellationToken: ct);
        await channel.QueueDeclareAsync(rabbit.Value.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(rabbit.Value.DeadLetterQueue, rabbit.Value.DeadLetterExchange, "#", cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, ct);
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: ct);

        // Stay connected until the channel dies (recovered automatically) or shutdown.
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { throw; }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        ConsumeOutcome outcome;
        try
        {
            var eventId = Guid.TryParse(ea.BasicProperties.MessageId, out var parsed)
                ? parsed
                : Guid.NewGuid(); // unparsable id: never dedupe, still process
            var payload = Encoding.UTF8.GetString(ea.Body.Span);
            var message = new OutboxEventMessage(eventId, ea.RoutingKey ?? "unknown", payload);
            var redeliveries = RedeliveryCount(ea);

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pipeline = new ConsumerPipeline(db, CreateConsumer(scope));
            outcome = await pipeline.HandleAsync(message, redeliveries, ct);
        }
        catch (OperationCanceledException)
        {
            outcome = ConsumeOutcome.Requeue; // shutdown: hand it back untouched
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Consumer {Queue} loop fault; requeueing", QueueName);
            outcome = ConsumeOutcome.Requeue;
        }

        switch (outcome)
        {
            case ConsumeOutcome.Ack:
            case ConsumeOutcome.AckDuplicate:
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                break;
            case ConsumeOutcome.Requeue:
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
                break;
            case ConsumeOutcome.DeadLetter:
                log.LogWarning("Poison message on {Queue} after {Max} redeliveries -> DLQ", QueueName, ConsumerPipeline.MaxRedeliveries);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                break;
        }
    }

    private int RedeliveryCount(BasicDeliverEventArgs ea)
    {
        // Broker-recorded redelivery count for OUR queue from x-death headers
        // (present only after a nack/requeue hop).
        var headers = ea.BasicProperties.Headers;
        if (headers is null || !headers.TryGetValue("x-death", out var raw)) return 0;
        if (raw is not List<object?> deaths) return 0;
        foreach (var death in deaths)
        {
            if (death is not Dictionary<string, object?> table) continue;
            var queue = table.TryGetValue("queue", out var q) ? AsString(q) : null;
            if (queue != QueueName) continue;
            if (table.TryGetValue("count", out var c) && c is not null)
                return Convert.ToInt32(c);
        }
        return 0;

        static string? AsString(object? v) => v switch
        {
            null => null,
            byte[] b => Encoding.UTF8.GetString(b),
            _ => v.ToString(),
        };
    }
}
