namespace Shop.UnitTests;

/// <summary>
/// A3 / ADR-0007 scope guard: suggestions stay deterministic and attribute-
/// driven — no vector store, embedding provider, LLM SDK, or behavior-based
/// recommendation dependency may sneak into the catalog milestone.
/// </summary>
public class A3ScopeTests
{
    private static readonly string[] ForbiddenPackageFragments =
    [
        "pgvector",
        "openai",
        "azure.ai",
        "microsoft.extensions.ai",
        "semantickernel",
        "langchain",
        "anthropic",
        "ollama",
        "huggingface",
    ];

    [Fact]
    public void A3_adds_no_vector_embedding_or_llm_dependency()
    {
        var root = RepoRoot();
        var projects = Directory.GetFiles(Path.Combine(root, "backend"), "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var text = File.ReadAllText(project);
            foreach (var forbidden in ForbiddenPackageFragments)
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A3_policy_does_not_read_behavior_history()
    {
        var root = RepoRoot();
        var sources = Directory.GetFiles(
            Path.Combine(root, "backend", "Shop.Application", "Specs"), "*.cs", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(sources);
        foreach (var source in sources)
            Assert.DoesNotContain("UserBehaviorEvent", File.ReadAllText(source), StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShopMind.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
