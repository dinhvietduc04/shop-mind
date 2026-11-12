using System.Text.Json;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Requirements;
using Shop.Application.Services;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.UnitTests;

/// <summary>
/// M13 / ADR-0007: the composition must make exactly one filtered-search call
/// through the A3 codec, explain only the filters it applied, and fail closed on
/// a hint it cannot resolve. No second search path, no invented filters.
/// </summary>
public class RequirementSearchServiceTests
{
    [Fact]
    public async Task Search_composes_one_query_and_explains_the_filters_it_applied()
    {
        var products = new FakeProducts(
            Product("ThinkPad", 1499m, ramGb: 16),
            Product("MacBook", 1099m, ramGb: 8));
        var service = new RequirementSearchService(products, new FakeCategories(LaptopCategory()));

        var result = await service.SearchAsync(
            new RequirementSearchRequest("laptop for backend dev under $1600"));

        var query = Assert.IsType<ProductQueryParams>(products.LastQuery);
        Assert.Equal("laptops", query.Category);
        Assert.Equal(1600m, query.MaxPrice);
        Assert.Equal("ram_gb:gte:16", query.Specs);
        Assert.False(query.IncludeInactive);

        Assert.Equal("laptops", result.Category);
        Assert.Equal(1600m, result.Budget);
        Assert.Contains("Category: Laptops", result.AppliedFilters);
        Assert.Contains("Price <= 1600 USD", result.AppliedFilters);
        Assert.Contains(
            result.AppliedFilters,
            filter => filter.Contains("RAM (GB) >= 16", StringComparison.Ordinal));
        Assert.Empty(result.Questions);

        Assert.Equal("ThinkPad", result.Items[0].Name);
        Assert.Contains("RAM (GB) >= 16 GB", result.Items[0].Why);
    }

    [Fact]
    public async Task A_fuzzy_requirement_returns_questions_instead_of_invented_filters()
    {
        var products = new FakeProducts(Product("ThinkPad", 1499m, ramGb: 16));
        var service = new RequirementSearchService(products, new FakeCategories(LaptopCategory()));

        var result = await service.SearchAsync(new RequirementSearchRequest("laptop for work"));

        Assert.Null(products.LastQuery!.Specs);
        var question = Assert.Single(result.Questions);
        Assert.Equal("ram_gb", question.Key);
    }

    [Fact]
    public async Task An_unknown_category_hint_fails_closed()
    {
        var products = new FakeProducts(Product("ThinkPad", 1499m, ramGb: 16));
        var service = new RequirementSearchService(products, new FakeCategories(LaptopCategory()));

        await Assert.ThrowsAsync<DomainValidationException>(() => service.SearchAsync(
            new RequirementSearchRequest("anything", CategoryHint: "toasters")));

        Assert.Null(products.LastQuery);
    }

    [Fact]
    public async Task An_empty_requirement_is_rejected_before_any_search()
    {
        var products = new FakeProducts(Product("ThinkPad", 1499m, ramGb: 16));
        var service = new RequirementSearchService(products, new FakeCategories(LaptopCategory()));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.SearchAsync(new RequirementSearchRequest("  ")));

        Assert.Null(products.LastQuery);
    }

    private static CategoryDto LaptopCategory() =>
        new(Guid.NewGuid(), "Laptops", "laptops", null, IsActive: true);

    private static ProductDto Product(string name, decimal price, double ramGb)
    {
        using var document = JsonDocument.Parse($$"""{"ram_gb": {{ramGb}}}""");

        return new ProductDto(
            Guid.NewGuid(),
            name,
            name.ToLowerInvariant(),
            $"SKU-{name}",
            "Brand",
            price,
            ProductStatus.Active,
            Guid.NewGuid(),
            "Laptops",
            AvailableStock: 5,
            [],
            document.RootElement.Clone());
    }

    private sealed class FakeProducts(params ProductDto[] products) : IProductService
    {
        public ProductQueryParams? LastQuery { get; private set; }

        public Task<PagedResult<ProductDto>> QueryAsync(
            ProductQueryParams query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new PagedResult<ProductDto>
            {
                Items = products,
                Page = 1,
                PageSize = query.PageSize,
                TotalCount = products.Length,
            });
        }

        public Task<IReadOnlyList<FacetDto>> GetFacetsAsync(
            string? category, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FacetDto>>(
            [
                new FacetDto("ram_gb", "RAM (GB)", "range", "GB", null, Min: 8, Max: 32, Count: 2),
            ]);

        public Task<ProductDetailDto> GetByIdAsync(Guid id, bool includeInactive = false, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<ProductDto>> ListAdminAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCategories(params CategoryDto[] categories) : ICategoryService
    {
        public Task<IReadOnlyList<CategoryDto>> ListPublicAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CategoryDto>>(categories);

        public Task<CategoryDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CategoryDto>> ListAdminAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
