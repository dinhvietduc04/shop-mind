using Microsoft.Extensions.Options;
using Shop.Infrastructure.Messaging;
using Shop.Infrastructure.Persistence;

namespace Shop.Worker;

/// <summary>B4 (M11): periodic outbox growth control — Sent rows past the horizon go.</summary>
public sealed class OutboxRetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<OutboxRetentionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, o.IntervalHours)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var removed = await new OutboxRetentionService(db)
                    .PurgeSentAsync(DateTime.UtcNow.AddDays(-o.KeepDays), stoppingToken);
                if (removed > 0)
                    log.LogInformation("Outbox retention purged {Count} Sent rows older than {Days} days", removed, o.KeepDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Outbox retention pass failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
