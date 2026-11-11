using System.Net;

namespace Shop.Domain.Exceptions;

/// <summary>
/// A4: the one failure type the AI seam exposes. <see cref="Retryable"/> is the
/// provider's honest answer (429, 5xx, network, timeout) so A5/A7 can decide
/// without parsing messages or exception types.
/// </summary>
public sealed class AiProviderException : ShopDomainException
{
    public const string NotConfiguredCode = "AI_NOT_CONFIGURED";
    public const string ProviderErrorCode = "AI_PROVIDER_ERROR";

    public bool Retryable { get; }

    private AiProviderException(string code, string message, bool retryable, Exception? inner = null)
        : base(code, message, inner) => Retryable = retryable;

    /// <summary>No key for a preset that requires one, or an unknown preset.</summary>
    public static AiProviderException NotConfigured(string provider) =>
        new(NotConfiguredCode,
            $"AI provider '{provider}' is not configured. Set Ai:ApiKey (env Ai__ApiKey) or Ai:Provider.",
            retryable: false);

    public static AiProviderException ProviderError(HttpStatusCode status) =>
        new(ProviderErrorCode,
            $"AI provider returned HTTP {(int)status}.",
            retryable: IsRetryableStatus(status));

    /// <summary>One retry rule shared by the adapter's retry loop and its terminal error.</summary>
    public static bool IsRetryableStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    public static AiProviderException Malformed(string detail) =>
        new(ProviderErrorCode, $"AI provider returned a malformed response ({detail}).", retryable: false);

    public static AiProviderException Unreachable(Exception inner) =>
        new(ProviderErrorCode, "AI provider is unreachable.", retryable: true, inner);

    public static AiProviderException TimedOut() =>
        new(ProviderErrorCode, "AI provider timed out.", retryable: true);
}
