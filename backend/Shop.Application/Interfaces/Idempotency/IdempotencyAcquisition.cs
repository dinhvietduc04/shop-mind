namespace Shop.Application.Interfaces;

public sealed record IdempotencyAcquisition(
    IdempotencyAcquisitionResult Result,
    string? LeaseToken = null,
    int? RetryAfterSeconds = null,
    StoredIdempotentResponse? StoredResponse = null);
