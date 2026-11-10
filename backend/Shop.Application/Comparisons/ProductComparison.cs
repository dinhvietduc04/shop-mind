using System.Text.Json;

namespace Shop.Application.Comparisons;

/// <summary>
/// A2: a deterministic side-by-side product comparison. The agent narrates it;
/// nothing in the producer synthesizes prose, ranks, or exposes reserved stock.
/// Cells align to <see cref="Products"/> by index; a missing spec is null, not
/// false, because an absent key is different from a stored false.
/// </summary>
public sealed record ProductComparison(
    IReadOnlyList<ComparedProduct> Products,
    IReadOnlyList<ComparisonRow> Rows);

public sealed record ComparedProduct(
    Guid Id,
    string Name,
    string SKU,
    string? Brand,
    string? Category,
    decimal Price,
    int AvailableStock,
    string? ImageUrl);

public sealed record ComparisonRow(
    string Key,
    string Label,
    string? Unit,
    IReadOnlyList<JsonElement?> Cells);
