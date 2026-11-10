using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;
using Shop.Mcp.Tooling;

namespace Shop.IntegrationTests;

/// <summary>
/// A2 / ADR-0004: the MCP idempotency guard is fail-closed. When the store
/// cannot answer, a write tool returns a retryable error and performs no work.
/// </summary>
public class McpIdempotencyOutageTests(DeadRedisMcpFactory factory) : IClassFixture<DeadRedisMcpFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Write_tool_refuses_when_the_idempotency_store_is_down()
    {
        var (userId, token) = await CreateUserWithTokenAsync("outage");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "outage-1",
            });

        Assert.True(result.IsError);
        var error = JsonSerializer.Deserialize<McpToolError>(Text(result), Json)!;
        Assert.Equal(McpErrorCodes.IdempotencyUnavailable, error.Code);
        Assert.True(error.Retryable);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.CartItems.AsNoTracking()
            .CountAsync(item => item.Cart!.UserId == userId));
    }

    private async Task<(Guid UserId, string Token)> CreateUserWithTokenAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Email = $"{name}-{Guid.NewGuid():N}@mcp.test",
            PasswordHash = "not-used",
            FirstName = name,
            LastName = "Test",
            Role = UserRole.Customer,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (user.Id, TestAgentTokens.AgentToken(user.Id));
    }

    private async Task<Guid> FirstProductIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.Select(p => p.Id).FirstAsync();
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
