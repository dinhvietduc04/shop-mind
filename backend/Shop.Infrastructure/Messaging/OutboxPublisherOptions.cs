namespace Shop.Infrastructure.Messaging;

/// <summary>B4 publisher loop knobs (M11: poll 2s, batch 50, 3 attempts).</summary>
public sealed class OutboxPublisherOptions
{
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 3;
    public int PollSeconds { get; set; } = 2;
}
