using System.Security.Cryptography;
using System.Text;
using Shop.Application.Dtos;
using Shop.Application.Events;
using Shop.Application.Interfaces;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Infrastructure.Payments;

/// <summary>
/// B5 (ADR-0003): the dev/test PSP. Confirmation on its hosted page issues a
/// signed webhook event through the normal intake (same enqueue path as a real
/// PSP's server-to-server POST), so the ONLY money-apply code is the webhook
/// consumer — no second money path. Signature: HMAC-SHA256 hex over the raw
/// JSON body with the configured dev secret (Stripe's adapter replaces this at
/// milestone 13 behind the same IPaymentProvider seam).
/// </summary>
public sealed class FakePspProvider(IEventPublisher events, string webhookSecret) : IPaymentProvider
{
    public string Name => "fake-psp";

    public async Task IssueSuccessWebhookAsync(PaySessionView session, CancellationToken ct = default)
    {
        if (session.Provider != Name)
            throw new DomainValidationException($"Session belongs to provider '{session.Provider}'.");
        var evt = new PaymentProviderEvent(
            Name,
            $"fake-{Guid.NewGuid():N}",
            session.SessionToken,
            PaymentOutcome.Captured,
            ToMinorUnits(session.Amount),
            session.Currency,
            DateTime.UtcNow,
            "fake.checkout.session.completed");
        await EnqueueIntakeAsync(evt, ct);
    }

    public PaymentProviderEvent VerifyAndNormalize(string rawBody, string signature)
    {
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(rawBody)));
        byte[] given;
        try
        {
            given = Convert.FromHexString(signature);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new WebhookSignatureException("malformed signature header");
        }
        if (given.Length != expected.Length / 2
            || !CryptographicOperations.FixedTimeEquals(given, Convert.FromHexString(expected)))
            throw new WebhookSignatureException("HMAC mismatch");

        var evt = System.Text.Json.JsonSerializer.Deserialize<PaymentProviderEvent>(rawBody, Wire)
            ?? throw new WebhookSignatureException("empty body");
        return evt with { Provider = Name };
    }

    /// <summary>Signs a normalized event exactly as a fake PSP POST would (used by intake tests + confirm).</summary>
    public string ComputeSignature(string rawBody)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(rawBody)));

    /// <summary>Shared path: enqueue the normalized event for the worker consumer (M10 machinery).</summary>
    public async Task EnqueueIntakeAsync(PaymentProviderEvent evt, CancellationToken ct)
    {
        await events.EnqueueAsync(new Shop.Domain.Events.PaymentWebhookReceivedEvent(
            Shop.Domain.ProviderEventIds.ToGuid(evt.ProviderEventId),
            evt.Provider, evt.ProviderEventId, evt.SessionToken,
            evt.Outcome.ToString(), evt.AmountMinor, evt.Currency, evt.OccurredAt, evt.RawType), ct);
    }

    public static long ToMinorUnits(decimal amount) => (long)decimal.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static readonly System.Text.Json.JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };
}
