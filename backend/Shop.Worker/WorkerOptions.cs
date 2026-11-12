namespace Shop.Worker;

/// <summary>B4: broker topology per M11 contracts — topic shop.events, DLX, queues.</summary>
public sealed class RabbitMqOptions
{
    public string Url { get; set; } = "amqp://guest:guest@localhost:5672";
    public string Exchange { get; set; } = "shop.events";
    public string DeadLetterExchange { get; set; } = "shop.events.dlx";
    public string DeadLetterQueue { get; set; } = "shop.events.dlq";
    public string ClientName { get; set; } = "shopmind-worker";
}

/// <summary>B4: publisher loop settings (bound from "Outbox").</summary>
public sealed class WorkerOutboxOptions
{
    public int PollSeconds { get; set; } = 2;
    public int MaxBackoffSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>B4: outbox retention (bound from "OutboxRetention").</summary>
public sealed class RetentionOptions
{
    public int KeepDays { get; set; } = 30;
    public int IntervalHours { get; set; } = 24;
}

/// <summary>B4: collected-data retention (bound from "Retention").</summary>
public sealed class DataRetentionOptions
{
    /// <summary>M18 schema: behavior events are kept 12 months.</summary>
    public int BehaviorEventKeepDays { get; set; } = 365;
    public int IntervalHours { get; set; } = 24;
}
