using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Shop.IntegrationTests;

[CollectionDefinition("MCP STDIO", DisableParallelization = true)]
public sealed class McpStdioCollection
{
}

/// <summary>
/// A1: the STDIO transport is a real process on the same Application services.
/// It must keep stdout clean for the protocol and expose the same manifest.
/// </summary>
[Collection("MCP STDIO")]
public class McpStdioTests : IAsyncLifetime
{
    private readonly McpWebFactory _factory = new();

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Stdio_transport_lists_tools_and_calls_search_products()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ConnectionStrings__Default"] = _factory.DatabaseConnectionString,
            ["Redis__ConnectionString"] = _factory.Redis.ConnectionString,
            ["Redis__Enabled"] = "false",
            ["Mcp__KeyPrefix"] = _factory.Redis.KeyPrefix + "stdio:mcp:",
            ["Jwt__Secret"] = TestAgentTokens.Secret,
            ["Jwt__Issuer"] = TestAgentTokens.Issuer,
            ["Mcp__Audience"] = TestAgentTokens.Audience,
        };

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "ShopMind MCP (stdio)",
            Command = "dotnet",
            Arguments = ["run", "--project", "backend/Shop.Mcp", "--no-build", "--", "--transport", "stdio"],
            WorkingDirectory = RepoRoot(),
            EnvironmentVariables = environment,
            InheritEnvironmentVariables = true,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
        });

        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "search_products");

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["category"] = "laptops" });

        Assert.False(result.IsError);
        Assert.Contains(
            "ThinkPad X1 Carbon Gen 11",
            result.Content.OfType<TextContentBlock>().Single().Text,
            StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShopMind.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
