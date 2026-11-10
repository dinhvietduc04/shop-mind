using Shop.Application.Events;

namespace Shop.Application.Interfaces;

/// <summary>
/// B5 (ADR-0003): the PSP seam. FakePsp serves dev/tests today; Stripe test-mode
/// lands at milestone 13 behind this same shape — a new parser at the webhook
/// edge, zero change to the money-path consumers (grilled decision).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>"fake-psp" | "stripe" — stored on PaymentSession.Provider's wire name.</summary>
    string Name { get; }

    /// <summary>
    /// Dev flow only: confirmation on the FakePsp page IS the provider's action —
    /// issue the success webhook through the normal intake (the real world's
    /// server-to-server webhook, simulated). Stripe overrides this to throw
    /// until milestone 13 replaces it with redirect + external webhook.
    /// </summary>
    Task IssueSuccessWebhookAsync(Dtos.PaySessionView session, CancellationToken ct = default);

    /// <summary>
    /// Verify a raw webhook POST (per-adapter signature) and normalize it to the
    /// locked provider-event contract. Throws WebhookSignatureException on any
    /// verification failure (intake answers 401; nothing is enqueued).
    /// </summary>
    PaymentProviderEvent VerifyAndNormalize(string rawBody, string signature);
}
