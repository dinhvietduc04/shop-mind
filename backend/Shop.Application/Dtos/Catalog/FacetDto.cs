namespace Shop.Application.Dtos;

/// <summary>
/// A3 / §B5+ 4: one facet derived from the registry ∩ populated Active data.
/// Bool specs are never facets; they only appear in the product's Specs map.
/// </summary>
public record FacetDto(
    string Key,
    string Label,
    string Type,
    string? Unit,
    IReadOnlyList<string>? Values,
    double? Min,
    double? Max,
    int Count);
