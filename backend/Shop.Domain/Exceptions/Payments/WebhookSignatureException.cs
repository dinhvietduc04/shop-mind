namespace Shop.Domain.Exceptions;

/// <summary>B5: webhook payload signature failed verification — intake refuses, nothing enqueued.</summary>
public class WebhookSignatureException : ShopDomainException
{
    public WebhookSignatureException(string reason)
        : base("WebhookSignatureInvalid", $"Webhook signature verification failed. ({reason})") { }
}
