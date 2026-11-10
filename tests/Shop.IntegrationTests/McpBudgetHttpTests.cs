using System.Text.Json;
using ModelContextProtocol.Protocol;
using Shop.Mcp.Tooling;

namespace Shop.IntegrationTests;

/// <summary>Small READ budget so the HTTP-level MCP budget path is exercised.</summary>
public sealed class McpBudgetWebFactory : McpWebFactory
{
    public McpBudgetWebFactory()
    {
        ReadPerMin = 2;
    }
}

/// <summary>
/// A1 / B3-L2: the MCP server enforces its own READ budget, independent of the
/// REST buckets, and returns the structured RATE_LIMITED envelope.
/// </summary>
public class McpBudgetHttpTests(McpBudgetWebFactory factory) : IClassFixture<McpBudgetWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Third_read_in_a_minute_is_rate_limited()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        for (var i = 0; i < 2; i++)
        {
            var allowed = await client.CallToolAsync(
                "search_products",
                new Dictionary<string, object?> { ["pageSize"] = 1 });
            Assert.False(allowed.IsError);
        }

        var denied = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["pageSize"] = 1 });

        Assert.True(denied.IsError);
        var error = JsonSerializer.Deserialize<McpToolError>(
            denied.Content.OfType<TextContentBlock>().Single().Text, Json);

        Assert.NotNull(error);
        Assert.Equal(McpErrorCodes.RateLimited, error!.Code);
        Assert.True(error.Retryable);
        Assert.Contains("Retry after", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
