using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shop.Application.Interfaces;

namespace Shop.Infrastructure.Auth;

public sealed class AgentTokenMinter(IOptions<JwtOptions> options) : IAgentTokenMinter
{
    public string Mint(Guid userId, string role, TimeSpan requestedTtl)
    {
        var opt = options.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var ttl = TimeSpan.FromSeconds(Math.Clamp(requestedTtl.TotalSeconds, 1, 60));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("scope", "agent"),
            new Claim("aud", "Shop.Mcp"),
        };

        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: "Shop.Mcp",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(ttl),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
