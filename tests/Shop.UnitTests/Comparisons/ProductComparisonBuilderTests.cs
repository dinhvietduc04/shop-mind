using System.Text.Json;
using Shop.Application.Common;
using Shop.Application.Comparisons;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.UnitTests.Comparisons;

/// <summary>
/// A2: compare is a deterministic table, not prose. These tests pin the shape
/// the agent narrates: caller order, registry order, null for a missing spec,
/// no unregistered keys, and no reserved-stock leak.
/// </summary>
public class ProductComparisonBuilderTests
{
    [Fact]
    public async Task Rows_are_registry_ordered_and_aligned_to_the_caller_product_order()
    {
        var thinkpad = Product("ThinkPad", 1500m, 4,
            """{"ram_gb":16,"storage_gb":512,"thunderbolt":true}""");
        var macbook = Product("MacBook", 1700m, 2,
            """{"ram_gb":16,"storage_gb":256}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(macbook, thinkpad));

        var comparison = await builder.BuildAsync([thinkpad.Id, macbook.Id]);

        Assert.Equal(new[] { thinkpad.Id, macbook.Id }, comparison.Products.Select(p => p.Id));
        Assert.Equal(new[] { "ram_gb", "storage_gb", "thunderbolt" }, comparison.Rows.Select(r => r.Key));

        var ram = comparison.Rows.Single(r => r.Key == "ram_gb");
        Assert.Equal("RAM (GB)", ram.Label);
        Assert.Equal("GB", ram.Unit);
        Assert.Equal(new[] { 16d, 16d }, ram.Cells.Select(c => c!.Value.GetDouble()));

        // A missing bool is null, never false: absent and false are different facts.
        var thunderbolt = comparison.Rows.Single(r => r.Key == "thunderbolt");
        Assert.True(thunderbolt.Cells[0]!.Value.GetBoolean());
        Assert.Null(thunderbolt.Cells[1]);
    }

    [Fact]
    public async Task Unregistered_jsonb_keys_are_dropped()
    {
        var product = Product("Laptop", 1000m, 1, """{"ram_gb":8,"made_up_key":42}""");
        var other = Product("Laptop 2", 1100m, 1, """{"ram_gb":16}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(product, other));

        var comparison = await builder.BuildAsync([product.Id, other.Id]);

        Assert.Equal(new[] { "ram_gb" }, comparison.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task A_key_absent_from_every_product_is_not_a_row()
    {
        var a = Product("A", 1m, 1, """{"ram_gb":8}""");
        var b = Product("B", 2m, 1, """{"storage_gb":256}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(a, b));

        var comparison = await builder.BuildAsync([a.Id, b.Id]);

        Assert.Equal(new[] { "ram_gb", "storage_gb" }, comparison.Rows.Select(r => r.Key));
        var storage = comparison.Rows.Single(r => r.Key == "storage_gb");
        Assert.Null(storage.Cells[0]);
        Assert.Equal(256d, storage.Cells[1]!.Value.GetDouble());
    }

    [Fact]
    public async Task Product_header_carries_only_available_stock_and_first_image()
    {
        var product = Product("A", 12.5m, 3, """{"ram_gb":8}""");
        var other = Product("B", 13m, 4, """{"ram_gb":16}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(product, other));

        var comparison = await builder.BuildAsync([product.Id, other.Id]);

        var column = comparison.Products[0];
        Assert.Equal(12.5m, column.Price);
        Assert.Equal(3, column.AvailableStock);
        Assert.Equal("https://img.test/a.png", column.ImageUrl);
        Assert.DoesNotContain(
            typeof(ComparedProduct).GetProperties(),
            property => property.Name.Contains("Reserved", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Product_count_outside_two_to_four_is_rejected(int count)
    {
        var products = Enumerable.Range(0, 5)
            .Select(i => Product($"P{i}", i + 1, 1, """{"ram_gb":8}"""))
            .ToArray();
        var builder = new ProductComparisonBuilder(new FakeProducts(products));

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            builder.BuildAsync(products.Take(count).Select(p => p.Id).ToList()));
    }

    [Fact]
    public async Task Duplicate_product_ids_are_rejected()
    {
        var product = Product("A", 1m, 1, """{"ram_gb":8}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(product));

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            builder.BuildAsync([product.Id, product.Id]));
    }

    [Fact]
    public async Task A_missing_or_inactive_product_fails_the_whole_call()
    {
        var product = Product("A", 1m, 1, """{"ram_gb":8}""");
        var builder = new ProductComparisonBuilder(new FakeProducts(product));

        await Assert.ThrowsAsync<ProductNotFoundException>(() =>
            builder.BuildAsync([product.Id, Guid.NewGuid()]));
    }

    private static ProductDetailDto Product(
        string name, decimal price, int stock, string specsJson)
    {
        using var document = JsonDocument.Parse(specsJson);
        return new ProductDetailDto(
            Guid.NewGuid(),
            name,
            $"{name}-slug",
            $"{name}-sku",
            Description: null,
            Brand: "Lenovo",
            Price: price,
            Status: ProductStatus.Active,
            CategoryId: Guid.NewGuid(),
            CategoryName: "Laptops",
            AvailableStock: stock,
            Images: [new ProductImageDto(Guid.NewGuid(), $"https://img.test/{name.ToLowerInvariant()}.png", null, 0)],
            CreatedAt: DateTime.UtcNow,
            Specs: document.RootElement.Clone());
    }

    private sealed class FakeProducts(params ProductDetailDto[] products) : IProductService
    {
        private readonly Dictionary<Guid, ProductDetailDto> _byId = products.ToDictionary(p => p.Id);

        public Task<ProductDetailDto> GetByIdAsync(
            Guid id, bool includeInactive = false, CancellationToken ct = default) =>
            _byId.TryGetValue(id, out var product)
                ? Task.FromResult(product)
                : throw new ProductNotFoundException(id);

        public Task<PagedResult<ProductDto>> QueryAsync(ProductQueryParams query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FacetDto>> GetFacetsAsync(string? category, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<ProductDto>> ListAdminAsync(
            string? search, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProductDetailDto> UpdateAsync(
            Guid id, UpdateProductRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
