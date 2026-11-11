namespace Shop.Api.RateLimiting.Models;

public sealed record RateLimitPolicy (
    string Bucket,
    int Limit,
    TimeSpan Window,
    string Partition
);