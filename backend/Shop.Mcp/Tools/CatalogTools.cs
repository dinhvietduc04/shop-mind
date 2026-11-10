using System.ComponentModel;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Mcp.Tooling;

namespace Shop.Mcp.Tools;

/// <summary>
/// A1 public catalog tools. They are anonymous by policy, read-only, and reuse
/// the Application services — never the database context.
/// </summary>
[McpServerToolType]
public static class CatalogTools
{
    private static readonly HashSet<string> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        "price_asc", "price_desc", "newest", "name_asc", "name_desc",
    };

    [McpServerTool(Name = "search_products", Title = "Search products")]
    [Description("Search active products by text, category, brand, price, and stock.")]
    public static async Task<CallToolResult> SearchProducts(
        ToolExecutor executor,
        IProductService products,
        IOptions<McpOptions> options,
        [Description("Free-text query over name, SKU, and brand.")] string? query = null,
        [Description("Category slug, for example laptops.")] string? category = null,
        [Description("Brand name, for example Lenovo.")] string? brand = null,
        [Description("Minimum price in USD.")] decimal? minPrice = null,
        [Description("Maximum price in USD.")] decimal? maxPrice = null,
        [Description("When true, return only products with available stock.")] bool? inStockOnly = null,
        [Description("Sort: price_asc, price_desc, newest, name_asc, or name_desc.")] string? sort = null,
        [Description("One-based page number.")] int page = 1,
        [Description("Page size, 1 through 50.")] int pageSize = 20,
        CancellationToken ct = default)
    {
        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().ToLowerInvariant();
        var args = new ProductQueryParams(
            query, category, brand, minPrice, maxPrice, inStockOnly, normalizedSort,
            page, pageSize, IncludeInactive: false, Specs: null);

        return await executor.RunAsync(
            "search_products",
            new { query, category, brand, minPrice, maxPrice, inStockOnly, sort = normalizedSort, page, pageSize },
            async token =>
            {
                Validate(args, options.Value.MaxPageSize);
                return (object?)await products.QueryAsync(args, token);
            },
            ct);
    }

    [McpServerTool(Name = "get_product", Title = "Get product")]
    [Description("Get one active product by its UUID.")]
    public static async Task<CallToolResult> GetProduct(
        ToolExecutor executor,
        IProductService products,
        [Description("The product UUID.")] Guid productId,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_product",
            new { productId },
            async token =>
            {
                if (productId == Guid.Empty)
                    throw Invalid("productId", "productId is required.");
                return (object?)await products.GetByIdAsync(productId, includeInactive: false, ct: token);
            },
            ct);
    }

    [McpServerTool(Name = "get_product_facets", Title = "Get product facets")]
    [Description("Get the specification filters that are actually available for a category.")]
    public static async Task<CallToolResult> GetProductFacets(
        ToolExecutor executor,
        IProductService products,
        [Description("Category slug; omit it for all categories.")] string? category = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "get_product_facets",
            new { category },
            async token =>
            {
                if (category is { Length: > 100 })
                    throw Invalid("category", "category must be 100 characters or fewer.");
                return (object?)await products.GetFacetsAsync(category, token);
            },
            ct);
    }

    private static void Validate(ProductQueryParams args, int maxPageSize)
    {
        var failures = new List<ValidationFailure>();

        if (args.Page < 1)
            failures.Add(new ValidationFailure("page", "page must be at least 1."));
        if (args.PageSize < 1 || args.PageSize > maxPageSize)
            failures.Add(new ValidationFailure("pageSize", $"pageSize must be between 1 and {maxPageSize}."));
        if (args.MinPrice is < 0)
            failures.Add(new ValidationFailure("minPrice", "minPrice cannot be negative."));
        if (args.MaxPrice is < 0)
            failures.Add(new ValidationFailure("maxPrice", "maxPrice cannot be negative."));
        if (args.MinPrice.HasValue && args.MaxPrice.HasValue && args.MinPrice > args.MaxPrice)
            failures.Add(new ValidationFailure("minPrice", "minPrice cannot be greater than maxPrice."));
        if (args.Search is { Length: > 200 })
            failures.Add(new ValidationFailure("query", "query must be 200 characters or fewer."));
        if (args.Category is { Length: > 100 })
            failures.Add(new ValidationFailure("category", "category must be 100 characters or fewer."));
        if (args.Brand is { Length: > 100 })
            failures.Add(new ValidationFailure("brand", "brand must be 100 characters or fewer."));
        if (!string.IsNullOrWhiteSpace(args.Sort) && !Sorts.Contains(args.Sort))
            failures.Add(new ValidationFailure("sort", "sort must be one of: price_asc, price_desc, newest, name_asc, name_desc."));

        if (failures.Count > 0)
            throw new ValidationException(failures);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
