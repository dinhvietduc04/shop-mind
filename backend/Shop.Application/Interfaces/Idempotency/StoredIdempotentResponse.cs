namespace Shop.Application.Interfaces;

public sealed record StoredIdempotentResponse(int StatusCode, string ContentType, string Body);
