using System.Security.Claims;
using Shop.Application.Mcp;

namespace Shop.UnitTests.Mcp;

/// <summary>
/// A1 / ADR-0006: every tool call gets an identity context from the delegated
/// JWT (HTTP) or the explicit dev principal (STDIO). Anonymous is a valid
/// context only for the public READ tools.
/// </summary>
public class McpToolContextTests
{
    [Fact]
    public void Authenticated_principal_maps_user_role_scope_and_partition()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim("scope", "agent"),
        ], "test"));

        var context = McpToolContextFactory.FromPrincipal(
            principal, "corr-1", "idem-1", "127.0.0.1");

        Assert.Equal(userId, context.UserId);
        Assert.Equal("Customer", context.Role);
        Assert.Equal("agent", context.Scope);
        Assert.Equal("corr-1", context.CorrelationId);
        Assert.Equal("idem-1", context.IdempotencyKey);
        Assert.Equal($"user:{userId:D}", context.PartitionKey);
        Assert.True(context.IsAuthenticated);
        Assert.True(context.IsAgent);
    }

    [Fact]
    public void Anonymous_principal_uses_client_address_partition()
    {
        var context = McpToolContextFactory.FromPrincipal(null, null, null, "10.0.0.5");

        Assert.Null(context.UserId);
        Assert.Null(context.Role);
        Assert.Null(context.Scope);
        Assert.False(context.IsAuthenticated);
        Assert.False(context.IsAgent);
        Assert.Equal("anon:10.0.0.5", context.PartitionKey);
        Assert.False(string.IsNullOrWhiteSpace(context.CorrelationId));
    }

    [Fact]
    public void Invalid_correlation_id_is_replaced()
    {
        var context = McpToolContextFactory.FromPrincipal(null, "bad\nvalue", null, null);

        Assert.NotEqual("bad\nvalue", context.CorrelationId);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", context.CorrelationId);
    }

    [Fact]
    public void Stdio_dev_principal_maps_env_values()
    {
        var userId = Guid.NewGuid();
        var env = new Dictionary<string, string?>
        {
            ["MCP_DEV_USER_ID"] = userId.ToString(),
            ["MCP_DEV_ROLE"] = "Admin",
            ["MCP_DEV_SCOPE"] = "agent",
        };

        var context = McpToolContextFactory.FromStdio(name => env.GetValueOrDefault(name), null, null);

        Assert.Equal(userId, context.UserId);
        Assert.Equal("Admin", context.Role);
        Assert.Equal("agent", context.Scope);
        Assert.Equal($"user:{userId:D}", context.PartitionKey);
    }

    [Fact]
    public void Stdio_without_a_dev_user_is_anonymous()
    {
        var context = McpToolContextFactory.FromStdio(_ => null, null, null);

        Assert.Null(context.UserId);
        Assert.Equal("anon:stdio", context.PartitionKey);
    }
}
