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
    /// <summary>
    /// A3/A5: the ranking order in prose. The agent prompt reads this constant,
    /// so the instruction the model sees cannot drift from the deterministic
    /// ranker that actually orders the tool result.
    /// </summary>
    public const string RuleDescription =
        "Rank matches by facet-match count first, then in-stock before out-of-stock, " +
        "then ascending price, then product id. Never invent a model score, popularity " +
        "signal, or ranking that is not produced by a tool call.";

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
