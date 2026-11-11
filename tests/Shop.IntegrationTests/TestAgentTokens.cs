using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Shop.IntegrationTests;

/// <summary>A1: mints the delegated scope=agent token the AgentLoop will issue in A5.</summary>
public static class TestAgentTokens
{
    public const string Secret = "shopmind-integration-test-secret-32chars!!";
    public const string Issuer = "ShopMind";
    public const string Audience = "Shop.Mcp";

    public static string AgentToken(
        Guid userId,
        string role = "Customer",
        string audience = Audience,
        string scope = "agent")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("scope", scope),
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
