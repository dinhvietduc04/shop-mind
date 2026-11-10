using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shop.Application.Events;
using Shop.Application.Services;
using Shop.Infrastructure.Persistence;
using Shop.Worker.RabbitMq;

namespace Shop.Worker.Consumers;

/// <summary>
/// B5: the payment-events queue — verified webhook intake rows published by
/// the M11 outbox publisher arrive here and PaymentWebhookConsumer applies the
/// fate. NOT the pipeline base: dedupe + the whole transaction live inside
/// ApplyAsync, so this shell only does acking and the x-death → DLQ cap
/// (every terminal ApplyAsync outcome acks; faults requeue up to 3 hops).
/// </summary>
public sealed class PaymentWebhookQueueWorker(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> rabbit, ILogger<PaymentWebhookQueueWorker> log) : BackgroundService
{
    private const string QueueName = "shop.payment.webhook";
    private const int MaxRedeliveries = 3;

    private readonly ConnectionFactory _factory = new()
    {
        Uri = new Uri(rabbit.Value.Url),
        ClientProvidedName = $"{rabbit.Value.ClientName}-payment-webhook",
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
                log.LogWarning(ex, "Payment webhook broker link dropped; reconnecting in {Backoff}s", backoff);
                try { await Task.Delay(backoff, stoppingToken); } catch (OperationCanceledException) { return; }
                backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
            }
        }
    }

    private async Task RunConnectedAsync(CancellationToken ct)
    {
        await using var connection = await _factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(rabbit.Value.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(rabbit.Value.DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = rabbit.Value.DeadLetterExchange },
            cancellationToken: ct);
        await channel.QueueBindAsync(QueueName, rabbit.Value.Exchange, "payment.webhook-received", cancellationToken: ct);
        await channel.QueueDeclareAsync(rabbit.Value.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(rabbit.Value.DeadLetterQueue, rabbit.Value.DeadLetterExchange, "#", cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var evt = ParseIntake(Encoding.UTF8.GetString(ea.Body.Span));
                using var scope = scopes.CreateScope();
                var outcome = await scope.ServiceProvider
                    .GetRequiredService<PaymentWebhookConsumer>()
                    .ApplyAsync(evt, ct);
                log.LogInformation("Payment webhook {ProviderEventId} -> {Outcome}", evt.ProviderEventId, outcome);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                var poison = RedeliveryCount(ea) >= MaxRedeliveries;
                log.LogError(ex, "Payment webhook apply faulted; {Action}", poison ? "dead-lettering" : "requeueing");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: !poison, cancellationToken: CancellationToken.None);
            }
        };
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: ct);
        await Task.Delay(Timeout.Infinite, ct);
    }

    /// <summary>Unpack the payload-contract-v1 data object into the normalized provider event.</summary>
    internal static PaymentProviderEvent ParseIntake(string messageBody)
    {
        using var doc = JsonDocument.Parse(messageBody);
        var d = doc.RootElement.GetProperty("data");
        return new PaymentProviderEvent(
            d.GetProperty("provider").GetString() ?? "unknown",
            d.GetProperty("providerEventId").GetString() ?? "",
            d.GetProperty("sessionToken").GetString() ?? "",
            Enum.Parse<Shop.Domain.Enums.PaymentOutcome>(d.GetProperty("outcome").GetString() ?? "Failed"),
            d.GetProperty("amountMinor").GetInt64(),
            d.GetProperty("currency").GetString() ?? "USD",
            d.GetProperty("occurredAt").GetDateTime(),
            d.GetProperty("rawType").GetString() ?? "");
    }

    private static int RedeliveryCount(RabbitMQ.Client.Events.BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Headers is null || !ea.BasicProperties.Headers.TryGetValue("x-death", out var raw) || raw is not List<object?> deaths) return 0;
        foreach (var death in deaths)
            if (death is Dictionary<string, object?> table
                && table.TryGetValue("queue", out var q) && q?.ToString() == QueueName
                && table.TryGetValue("count", out var c) && c is not null)
                return Convert.ToInt32(c);
        return 0;
    }
}
