using Shop.Application.Specs;

namespace Shop.Application.Requirements;

/// <summary>
/// M13: structured-filter search output. Filters are the ONLY thing the
/// explanation may mention; questions come from the A3 elicitation policy and
/// are empty when the requirement was concrete enough to filter on.
/// </summary>
public sealed record RequirementSearchResult(
    string Requirement,
    string? Category,
    decimal? Budget,
    IReadOnlyList<string> AppliedFilters,
    IReadOnlyList<ElicitationQuestion> Questions,
    IReadOnlyList<RequirementMatchDto> Items,
    int TotalCount);

/// <summary>One ranked match with the item-specific facts behind its position.</summary>
public sealed record RequirementMatchDto(
    Guid ProductId,
    string Name,
    string Slug,
    string SKU,
    string? Brand,
    string? CategoryName,
    decimal Price,
    int AvailableStock,
    bool InStock,
    IReadOnlyList<string> Why);
