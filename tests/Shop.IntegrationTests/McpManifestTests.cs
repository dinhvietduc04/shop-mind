namespace Shop.IntegrationTests;

/// <summary>
/// A1 / ADR-0006: the runtime manifest is generated from the risk policy plus
/// the per-tool kill switches, so a disabled tool is not merely denied — it is
/// absent from tools/list.
/// </summary>
public class McpManifestTests(McpDisabledToolWebFactory factory) : IClassFixture<McpDisabledToolWebFactory>
{
    [Fact]
    public async Task Disabled_tools_are_absent_from_the_manifest()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var names = (await client.ListToolsAsync())
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("search_products", names);
        Assert.DoesNotContain("get_product", names);
        Assert.DoesNotContain("confirm_payment", names);
    }
}
