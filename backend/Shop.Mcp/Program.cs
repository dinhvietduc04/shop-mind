using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;
using Shop.Application.Mcp;

namespace Shop.Mcp;

/// <summary>
/// A1: one project, two transports. STDIO is for local dev/Inspector; HTTP is
/// the hosted Streamable HTTP endpoint at /mcp. Never write logs to stdout in
/// STDIO mode — stdout is the MCP protocol channel.
/// </summary>
public class McpProgram
{
    public static async Task Main(string[] args)
    {
        var transportIndex = Array.IndexOf(args, "--transport");
        var isStdio = transportIndex >= 0
                      && transportIndex + 1 < args.Length
                      && args[transportIndex + 1].Equals("stdio", StringComparison.OrdinalIgnoreCase);

        if (isStdio)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddShopMcp(builder.Configuration);
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync();
            return;
        }

        var web = WebApplication.CreateBuilder(args);
        web.Services.AddShopMcp(web.Configuration);
        web.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly();

        var jwt = web.Configuration.GetSection("Jwt");
        var jwtSecret = jwt.GetValue<string>("Secret") ?? "shopmind-dev-secret-key-change-me-please-32chars!";
        var jwtIssuer = jwt.GetValue<string>("Issuer") ?? "ShopMind";
        var mcpAudience = web.Configuration["Mcp:Audience"] ?? "Shop.Mcp";

        web.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = mcpAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = ClaimTypes.Role,
                };
            });
        web.Services.AddAuthorization();

        var app = web.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapMcp("/mcp");

        await app.RunAsync();
    }
}
