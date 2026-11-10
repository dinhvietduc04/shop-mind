using System.Text.Json;
using Shop.Application.Dtos;
using Shop.Application.Specs;

namespace Shop.UnitTests;

/// <summary>
/// A3 / ADR-0007: deterministic elicitation policy. The A5 agent loop will call
/// this before filtering; no LLM, embeddings or behavior history is involved.
/// </summary>
public class ElicitationPolicyTests
{
    [Fact]
    public void Fuzzy_request_without_facets_requires_discovery_and_filters_nothing()
    {
        var plan = ElicitationPolicy.BuildPlan("I need a laptop for work");

        Assert.True(plan.NeedsFacets);
        Assert.Empty(plan.Questions);
        Assert.Empty(plan.Filters);
        Assert.Empty(plan.Explanation);
    }

    [Fact]
    public void Fuzzy_request_with_facets_asks_at_most_three_questions_before_filtering()
    {
        var facets = new[]
        {
            Facet("ram_gb", "range", 8, 32),
            Facet("storage_gb", "range", 128, 1024),
            Facet("screen_in", "range", 13, 27),
            Facet("cpu", "enum", values: ["Intel Core i7", "Apple M3"]),
        };

        var plan = ElicitationPolicy.BuildPlan("I need a laptop for work", facets, maxQuestions: 10);

        Assert.False(plan.NeedsFacets);
        Assert.Equal(3, plan.Questions.Count);
        Assert.Empty(plan.Filters);
        Assert.Empty(plan.Explanation);
    }

    [Fact]
    public void Explicit_requirements_are_preserved_and_explained_from_applied_filters_only()
    {
        var explicitRequirements = new[] { new SpecClause("ram_gb", SpecOp.Gte, 16d) };

        var plan = ElicitationPolicy.BuildPlan("cheap gaming laptop", facets: [], explicitRequirements);

        Assert.False(plan.NeedsFacets);
        Assert.Empty(plan.Questions);
        var filter = Assert.Single(plan.Filters);
        Assert.Equal(16d, filter.Value);
        var explanation = Assert.Single(plan.Explanation);
        Assert.Contains("RAM", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("16", explanation);
        Assert.DoesNotContain("cheap", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gaming", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hints_are_added_only_after_facet_discovery_and_never_override_explicit_requirements()
    {
        var facets = new[]
        {
            Facet("ram_gb", "range", 8, 32),
            Facet("cpu", "enum", values: ["Intel Core i7", "Apple M3"]),
        };
        var explicitRequirements = new[] { new SpecClause("cpu", SpecOp.Eq, "Intel Core i7") };

        var plan = ElicitationPolicy.BuildPlan("backend dev", facets, explicitRequirements);

        Assert.Equal(2, plan.Filters.Count);
        Assert.Contains(plan.Filters, f => f.Key == "cpu" && Equals(f.Value, "Intel Core i7"));
        Assert.Contains(plan.Filters, f => f.Key == "ram_gb" && Equals(f.Value, 16d));
        Assert.DoesNotContain(plan.Filters, f => f.Key == "cpu" && !Equals(f.Value, "Intel Core i7"));
    }

    [Fact]
    public void Hints_are_not_invented_without_facet_discovery()
    {
        var explicitRequirements = new[] { new SpecClause("cpu", SpecOp.Eq, "Intel Core i7") };

        var plan = ElicitationPolicy.BuildPlan("backend dev", facets: null, explicitRequirements);

        var filter = Assert.Single(plan.Filters);
        Assert.Equal("cpu", filter.Key);
    }

    private static FacetDto Facet(string key, string type, double? min = null, double? max = null,
        IReadOnlyList<string>? values = null)
    {
        var def = SpecRegistry.Find(key)!;
        return new FacetDto(key, def.Label, type, def.Unit, values, min, max,
            values?.Count ?? 1);
    }
}

/// <summary>A3: deterministic result ordering for the future agent surface.</summary>
public class SpecRankingTests
{
    [Fact]
    public void Ranking_is_match_count_then_stock_then_price_then_id()
    {
        var products = new[]
        {
            Product("00000000-0000-0000-0000-000000000001", 1200m, inStock: true, ram: 32, storage: 512),
            Product("00000000-0000-0000-0000-000000000002", 800m, inStock: true, ram: 16, storage: 256),
            Product("00000000-0000-0000-0000-000000000003", 900m, inStock: true, ram: 32, storage: 256),
            Product("00000000-0000-0000-0000-000000000004", 500m, inStock: false, ram: 16, storage: 512),
        };
        var clauses = new[]
        {
            new SpecClause("ram_gb", SpecOp.Gte, 16d),
            new SpecClause("storage_gb", SpecOp.Gte, 512d),
        };

        var ranked = SpecRanking.Rank(products, clauses);

        Assert.Equal(
            ["00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000004",
             "00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000003"],
            ranked.Select(p => p.Id.ToString()).ToArray());
    }

    [Fact]
    public void Ranking_ties_are_stable_across_calls()
    {
        var products = new[]
        {
            Product("00000000-0000-0000-0000-000000000002", 800m, true, 16, 512),
            Product("00000000-0000-0000-0000-000000000001", 800m, true, 16, 512),
        };
        var clauses = new[] { new SpecClause("ram_gb", SpecOp.Gte, 16d) };

        var first = SpecRanking.Rank(products, clauses).Select(p => p.Id).ToArray();
        var second = SpecRanking.Rank(products, clauses).Select(p => p.Id).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(["00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002"],
            first.Select(id => id.ToString()).ToArray());
    }

    private static RankableProduct Product(string id, decimal price, bool inStock, int ram, int storage) =>
        new(Guid.Parse(id), price, inStock,
            JsonSerializer.SerializeToElement(new { ram_gb = ram, storage_gb = storage }));
}
