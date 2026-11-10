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
/// A1 acceptance: the real MCP HTTP transport lists only the policy-approved
/// tools, anonymous catalog reads work, and user-scoped tools are ownership
/// checked through the delegated JWT.
/// </summary>
public class McpHttpTests(McpWebFactory factory) : IClassFixture<McpWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Manifest_lists_only_the_A1_read_tools()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["get_cart", "get_product", "get_product_facets", "search_products"],
            names);
    }

    [Fact]
    public async Task Search_products_works_anonymously_and_matches_seed_data()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["category"] = "laptops", ["pageSize"] = 50 });

        Assert.False(result.IsError);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_cart_without_a_token_is_unauthorized()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.Unauthorized, Error(result).Code);
    }

    [Fact]
    public async Task Get_cart_returns_only_the_token_owners_cart()
    {
        var (aliceId, aliceToken) = await CreateUserWithTokenAsync("alice");
        var (bobId, bobToken) = await CreateUserWithTokenAsync("bob");
        _ = bobId;
        var productId = await AddProductToCartAsync(aliceId);

        await using var alice = await McpTestClient.ConnectAsync(factory, aliceToken);
        await using var bob = await McpTestClient.ConnectAsync(factory, bobToken);

        var aliceResult = await alice.CallToolAsync("get_cart", new Dictionary<string, object?>());
        var bobResult = await bob.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.False(aliceResult.IsError);
        Assert.False(bobResult.IsError);
        Assert.Contains(productId.ToString(), Text(aliceResult), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(productId.ToString(), Text(bobResult), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_tool_argument_is_rejected()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["bogus"] = 1 });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Oversized_arguments_are_rejected()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["query"] = new string('x', 5_000) });

        Assert.True(result.IsError);
        var error = Error(result);
        Assert.Equal(McpErrorCodes.InvalidArgs, error.Code);
        Assert.Contains("4096", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_product_returns_the_seeded_detail()
    {
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product",
            new Dictionary<string, object?> { ["productId"] = productId.ToString() });

        Assert.False(result.IsError);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_facets_work_anonymously()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product_facets",
            new Dictionary<string, object?> { ["category"] = "headphones" });

        Assert.False(result.IsError);
        Assert.Contains("battery_h", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_is_never_written_to_logs()
    {
        var (_, token) = await CreateUserWithTokenAsync("logcheck");
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        await client.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.DoesNotContain(
            TestLogSink.Instance.Messages,
            message => message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_page_size_is_invalid_args()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["pageSize"] = 51 });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Get_product_with_an_unknown_id_is_not_found()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product",
            new Dictionary<string, object?> { ["productId"] = Guid.NewGuid().ToString() });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.NotFound, Error(result).Code);
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
        return await db.Products
            .Where(p => p.SKU == "LEN-X1C-G11")
            .Select(p => p.Id)
            .SingleAsync();
    }

    private async Task<Guid> AddProductToCartAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = db.Products.OrderBy(p => p.Name).First();
        var cart = new Cart { UserId = userId };
        cart.Items.Add(new CartItem
        {
            ProductId = product.Id,
            Quantity = 1,
            UnitPrice = product.Price,
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        return product.Id;
    }

    private static McpToolError Error(CallToolResult result) =>
        JsonSerializer.Deserialize<McpToolError>(Text(result), Json)!;

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
