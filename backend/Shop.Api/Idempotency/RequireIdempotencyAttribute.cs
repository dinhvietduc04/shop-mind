namespace Shop.Api.Idempotency;

/// <summary>
/// B2 (ADR-0004): marks an endpoint as a money POST that requires an
/// <c>Idempotency-Key</c> (UUIDv4) header and dedupe via <c>IIdempotencyStore</c>.
/// Enforcement lives in <see cref="IdempotencyMiddleware"/>; the attribute is the
/// opt-in metadata it looks for. Applied to /api/checkout/quote when B5 lands it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyAttribute : Attribute
{
}
