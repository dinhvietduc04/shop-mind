namespace Shop.Application.Interfaces;

public sealed record IdempotencyRecord(
    string LeaseHolderToken,
    string RequestHash,
    IdempotencyState State,
    DateTimeOffset CreatedAt,
    StoredIdempotentResponse? Response = null);
