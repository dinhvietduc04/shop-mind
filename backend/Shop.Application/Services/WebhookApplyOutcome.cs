namespace Shop.Application.Services;

/// <summary>
/// B5 webhook apply verdicts. Applied = the fate is now order state (or was a
/// silent replay); Duplicate = provider event already processed; NoSession =
/// token unknown (deduped so it never retries); Anomaly = refused with a
/// PaymentAnomalies row + alert, human decides (ADR-0005).
/// </summary>
public enum WebhookApplyOutcome { Applied, Duplicate, NoSession, Anomaly }
