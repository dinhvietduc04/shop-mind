namespace Shop.Application.Specs;

/// <summary>A3: one targeted elicitation question derived from a discovered facet.</summary>
public sealed record ElicitationQuestion(
    string Key,
    string Label,
    string Type,
    string? Unit,
    IReadOnlyList<string>? Values,
    double? Min,
    double? Max);

/// <summary>
/// A3 / ADR-0007: what the agent should do next. <see cref="NeedsFacets"/> means
/// "call get_product_facets before filtering"; questions are capped at three;
/// filters are the only things an explanation may mention.
/// </summary>
public sealed record ElicitationPlan(
    bool NeedsFacets,
    IReadOnlyList<ElicitationQuestion> Questions,
    IReadOnlyList<SpecClause> Filters,
    IReadOnlyList<string> Explanation);
