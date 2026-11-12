using Microsoft.Extensions.Options;
using Shop.Infrastructure.Persistence;
using Shop.Infrastructure.Retention;

namespace Shop.Worker;

/// <summary>
/// B4: nightly data-retention pass. The outbox has its own worker; this one owns
/// collected analytics data. AI conversation retention joins here once those
/// tables exist (A5/A7).
/// </summary>
public sealed class RetentionSweeperWorker(
    IServiceScopeFactory scopes,
    IOptions<DataRetentionOptions> options,
    ILogger<RetentionSweeperWorker> log) : BackgroundService
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
                var removed = await new RetentionService(db)
                    .PurgeBehaviorEventsAsync(DateTime.UtcNow.AddDays(-o.BehaviorEventKeepDays), stoppingToken);
                if (removed > 0)
                    log.LogInformation(
                        "Retention purged {Count} behavior events older than {Days} days",
                        removed, o.BehaviorEventKeepDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Retention pass failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
