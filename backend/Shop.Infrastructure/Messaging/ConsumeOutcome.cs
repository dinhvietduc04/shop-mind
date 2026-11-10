namespace Shop.Infrastructure.Messaging;

/// <summary>What the AMQP layer should do with the message after the pipeline ran.</summary>
public enum ConsumeOutcome
{
    /// <summary>Processed; dedupe row committed.</summary>
    Ack,
    /// <summary>Already processed by this consumer; skip silently.</summary>
    AckDuplicate,
    /// <summary>Transient fault; redeliver (attempt counted by the broker).</summary>
    Requeue,
    /// <summary>Too many redeliveries; route to shop.events.dlq.</summary>
    DeadLetter,
}
