using Microsoft.Extensions.Options;
using Shop.Application.Interfaces.Events;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Persistence;

namespace Shop.Worker;

/// <summary>
/// B4 (M11): the outbox publisher loop. Each round runs in its own scope
/// (claim → publish → mark, one transaction); a broker fault doubles the wait
/// up to a cap — rows keep accumulating safely in Postgres (the API path is
/// untouched, the M10 guarantee) and drain when the broker returns.
/// </summary>
public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopes,
    IBrokerPublisher broker,
    IOptions<WorkerOutboxOptions> options,
    ILogger<OutboxPublisherWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var baseDelay = TimeSpan.FromSeconds(Math.Max(1, o.PollSeconds));
        var delay = baseDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var publisher = new OutboxPublisher(db, new OutboxClaimService(db), broker,
                    new OutboxPublisherOptions { BatchSize = o.BatchSize, MaxAttempts = o.MaxAttempts });

                var round = await publisher.RunOnceAsync(stoppingToken);
                if (round.Sent > 0)
                    log.LogInformation("Outbox round: {Sent} sent, {Failed} dead-lettered, {Pending} retrying",
                        round.Sent, round.Failed, round.Pending);
                if (round.BrokerFaulted)
                    log.LogWarning("Outbox round hit broker faults; backing off");

                delay = round.BrokerFaulted
                    ? TimeSpan.FromSeconds(Math.Min(o.MaxBackoffSeconds, delay.TotalSeconds * 2))
                    : baseDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // in-flight round rolled back: rows stay Pending, zero loss
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox publisher round faulted (rolled back); backing off");
                delay = TimeSpan.FromSeconds(Math.Min(o.MaxBackoffSeconds, delay.TotalSeconds * 2));
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
