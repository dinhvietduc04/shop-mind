using System.Text.Json;

namespace Shop.Application.Specs;

/// <summary>A3: the minimal product shape the deterministic ranker needs.</summary>
public sealed record RankableProduct(Guid Id, decimal Price, bool InStock, JsonElement Specs);

/// <summary>
/// A3 / ADR-0007: deterministic ranking — facet match count, then in-stock,
/// then price ascending, then id. No model score, no behavior history.
/// </summary>
public static class SpecRanking
{
    public static IReadOnlyList<RankableProduct> Rank(
        IEnumerable<RankableProduct> products, IReadOnlyList<SpecClause> clauses) =>
        products
            .Select(p => (Product: p, Matches: clauses.Count(c => SpecMatcher.Matches(p.Specs, c))))
            .OrderByDescending(x => x.Matches)
            .ThenByDescending(x => x.Product.InStock)
            .ThenBy(x => x.Product.Price)
            .ThenBy(x => x.Product.Id)
            .Select(x => x.Product)
            .ToList();
}
