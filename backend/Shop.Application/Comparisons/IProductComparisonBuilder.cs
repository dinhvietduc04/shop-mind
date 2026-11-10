namespace Shop.Application.Comparisons;

/// <summary>
/// A2: builds the compare table from public catalog reads plus the spec
/// registry. Pure producer; no EF, no LLM, no reserved-stock access.
/// </summary>
public interface IProductComparisonBuilder
{
    Task<ProductComparison> BuildAsync(IReadOnlyList<Guid> productIds, CancellationToken ct = default);
}
