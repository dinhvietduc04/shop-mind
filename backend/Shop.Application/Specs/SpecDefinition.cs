namespace Shop.Application.Specs;

/// <summary>
/// A3 / ADR-0007: one canonical product attribute. <see cref="Categories"/> is
/// the per-category registry membership; a key outside every category does not
/// exist for facets or filtered search.
/// </summary>
public sealed record SpecDefinition(
    string Key,
    string Label,
    SpecType Type,
    string? Unit,
    IReadOnlyList<string> Categories);
