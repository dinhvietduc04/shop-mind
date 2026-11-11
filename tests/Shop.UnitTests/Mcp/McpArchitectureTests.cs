using System.Reflection;
using ModelContextProtocol.Server;
using Shop.Application.Mcp;
using Shop.Mcp.Tools;

namespace Shop.UnitTests.Mcp;

/// <summary>
/// A1 / ADR-0006: the manifest cannot drift from the risk policy, and the MCP
/// project cannot reach the database directly. Both are build gates, not code
/// review conventions.
/// </summary>
public class McpArchitectureTests
{
    [Fact]
    public void Every_McpServerTool_has_a_risk_policy_entry()
    {
        var methods = typeof(CatalogTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var name = method.GetCustomAttribute<McpServerToolAttribute>()!.Name ?? method.Name;
            var policy = ToolRiskPolicy.Default.Find(name);

            Assert.NotNull(policy);
            Assert.NotEqual(ToolRiskClass.Never, policy!.RiskClass);
        }
    }

    [Fact]
    public void Shop_Mcp_has_no_direct_EF_or_Npgsql_dependency()
    {
        var root = RepoRoot();

        var sourceFiles = Directory
            .GetFiles(Path.Combine(root, "backend", "Shop.Mcp"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Npgsql", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\bAppDbContext\b", text);
            Assert.DoesNotMatch(@"\bDbContext\b", text);
        }

        var project = File.ReadAllText(Path.Combine(root, "backend", "Shop.Mcp", "Shop.Mcp.csproj"));
        Assert.DoesNotContain("EntityFrameworkCore", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Shop.Api", project, StringComparison.OrdinalIgnoreCase);
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
