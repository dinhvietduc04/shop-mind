namespace Shop.Infrastructure.Auth;

public class JwtOptions
{
    public string Secret { get; set; } = "shopmind-dev-secret-key-change-me-please-32chars!";
    public string Issuer { get; set; } = "ShopMind";
    public string Audience { get; set; } = "ShopMind";
    public int ExpiryMinutes { get; set; } = 120;
}
