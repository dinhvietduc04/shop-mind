using System.Text;
using RabbitMQ.Client;
using Shop.Application.Interfaces.Events;

namespace Shop.Worker.RabbitMq;

/// <summary>
/// B4 broker leg: publishes outbox payloads to the topic exchange with
/// messageId = eventId and publisher confirms. Any fault throws — the outbox
/// publisher keeps the row Pending (at-least-once; consumers dedupe).
/// Lazy, self-healing connection: created on demand, RabbitMQ.Client's
/// automatic recovery reconnects after blips.
/// </summary>
public sealed class RabbitMqPublisher : IBrokerPublisher, IAsyncDisposable
{
    private readonly ConnectionFactory factory;
    private readonly string exchange;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public RabbitMqPublisher(Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options)
    {
        factory = new ConnectionFactory
        {
            Uri = new Uri(options.Value.Url),
            ClientProvidedName = options.Value.ClientName,
            AutomaticRecoveryEnabled = true,
        };
        exchange = options.Value.Exchange;
    }

    private async Task<IChannel> ChannelAsync(CancellationToken ct)
    {
        if (channel is { IsOpen: true }) return channel;
        await gate.WaitAsync(ct);
        try
        {
            if (channel is { IsOpen: true }) return channel;
            channel?.Dispose();
            connection = await factory.CreateConnectionAsync(ct);
            // v7 confirm mode: with tracking enabled, the BasicPublishAsync task
            // completes on broker ack and throws on nack — confirms need no polling.
            var chan = await connection.CreateChannelAsync(new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true), cancellationToken: ct);
            await chan.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
            channel = chan;
            return chan;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PublishAsync(string routingKey, Guid messageId, string payloadJson, CancellationToken ct = default)
    {
        var chan = await ChannelAsync(ct);
        var props = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Type = routingKey,
            Persistent = true,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };
        await chan.BasicPublishAsync(exchange, routingKey, mandatory: false, props, Encoding.UTF8.GetBytes(payloadJson), ct);
        // (confirmation awaited inside the publish task by tracking — see ChannelAsync)
    }

    public async ValueTask DisposeAsync()
    {
        try { if (channel is not null) await channel.DisposeAsync(); } catch { }
        try { if (connection is not null) await connection.DisposeAsync(); } catch { }
        gate.Dispose();
    }
}
