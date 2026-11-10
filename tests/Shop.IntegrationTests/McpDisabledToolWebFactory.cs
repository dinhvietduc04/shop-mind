using Microsoft.AspNetCore.Hosting;

namespace Shop.IntegrationTests;

/// <summary>A1 kill-switch proof: a disabled tool is absent from the manifest.</summary>
public sealed class McpDisabledToolWebFactory : McpWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Tools:get_product:Enabled", "false");
    }
}
