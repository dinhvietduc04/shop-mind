namespace Shop.Infrastructure.Messaging;

/// <summary>Outcome tally of one round (worker logging + tests).</summary>
public sealed record OutboxPublishRound(
    int Claimed,
    int Sent,
    int Failed,
    int Pending,
    bool BrokerFaulted);
