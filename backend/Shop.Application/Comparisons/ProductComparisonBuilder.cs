using System.Text.Json;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Specs;
using Shop.Domain.Exceptions;

namespace Shop.Application.Comparisons;

/// <summary>
/// A2: the single compare producer. It reads each product through the public
/// catalog service (Active only) in caller order, then emits registry rows that
/// at least one compared product carries. Unregistered jsonb keys are dropped,
/// matching facets and filtered search. Reserved stock never leaves the
/// inventory aggregate, so <c>AvailableStock</c> is the only stock number here.
/// </summary>
public sealed class ProductComparisonBuilder(IProductService products) : IProductComparisonBuilder
{
    public const int MinProducts = 2;
    public const int MaxProducts = 4;

    public async Task<ProductComparison> BuildAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken ct = default)
    {
        if (productIds.Count is < MinProducts or > MaxProducts)
            throw new DomainValidationException(
                $"compare_products needs between {MinProducts} and {MaxProducts} product ids.");

        if (productIds.Distinct().Count() != productIds.Count)
            throw new DomainValidationException("compare_products product ids must be unique.");

        var details = new List<ProductDetailDto>(productIds.Count);
        foreach (var id in productIds)
            details.Add(await products.GetByIdAsync(id, includeInactive: false, ct: ct));

        return new ProductComparison(
            details.Select(ToColumn).ToList(),
            BuildRows(details));
    }

    private static ComparedProduct ToColumn(ProductDetailDto product) => new(
        product.Id,
        product.Name,
        product.SKU,
        product.Brand,
        product.CategoryName,
        product.Price,
        product.AvailableStock,
        product.Images.OrderBy(image => image.DisplayOrder).FirstOrDefault()?.Url);

    private static IReadOnlyList<ComparisonRow> BuildRows(IReadOnlyList<ProductDetailDto> details)
    {
        var present = details
            .SelectMany(SpecKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ComparisonRow>();
        foreach (var definition in SpecRegistry.All)
        {
            if (!present.Contains(definition.Key)) continue;

            var cells = details
                .Select(detail => ReadCell(detail.Specs, definition.Key))
                .ToList();
            rows.Add(new ComparisonRow(definition.Key, definition.Label, definition.Unit, cells));
        }

        return rows;
    }

    private static IEnumerable<string> SpecKeys(ProductDetailDto product) =>
        product.Specs.ValueKind == JsonValueKind.Object
            ? product.Specs.EnumerateObject().Select(property => property.Name)
            : [];

    private static JsonElement? ReadCell(JsonElement specs, string key)
    {
        if (specs.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in specs.EnumerateObject())
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                return property.Value;

        return null;
    }
}
