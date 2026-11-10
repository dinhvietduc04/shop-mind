namespace Shop.Domain.Entities;

/// <summary>
/// B5 (ADR-0001/0002): one payment attempt on the hosted page, bound to one
/// Quote (Order) and its owner. The 128-bit SessionToken is a capability
/// credential: it authorizes VIEWING the bill only; confirm needs the human's
/// own JWT. Single-use, dies with the Quote.
/// </summary>
public class PaymentSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>URL-safe 128-bit token. Unique — it IS the pay-page capability.</summary>
    public string SessionToken { get; set; } = string.Empty;

    public Shop.Domain.Enums.PaymentProvider Provider { get; set; } = Shop.Domain.Enums.PaymentProvider.FakePsp;

    /// <summary>One session per quote order (unique FK).</summary>
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Amount the provider must present and the webhook must match (minor-unit safety net).</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Domain.Entities.Order? Order { get; set; }

    /// <summary>
    /// B5 (ADR-0002): URL-safe 128-bit capability token (22 base64url chars).
    /// The value itself authorizes VIEWING the bill — nothing else.
    /// </summary>
    public static string GenerateSessionToken()
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
