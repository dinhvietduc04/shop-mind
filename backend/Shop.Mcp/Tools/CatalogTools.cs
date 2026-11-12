using System.ComponentModel;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Comparisons;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Application.Requirements;
using Shop.Domain.Exceptions;
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
    [Description("Search active products by text, category, brand, price, stock, and specs.")]
    public static async Task<CallToolResult> SearchProducts(
        ToolExecutor executor,
        IProductService products,
        IOptions<McpOptions> options,
        [Description("Free-text query over name and SKU.")] string? query = null,
        [Description("Category slug, for example laptops.")] string? category = null,
        [Description("Brand name, for example Lenovo.")] string? brand = null,
        [Description("Minimum price in USD.")] decimal? minPrice = null,
        [Description("Maximum price in USD.")] decimal? maxPrice = null,
        [Description("When true, return only products with available stock.")] bool? inStockOnly = null,
        [Description("Spec filters as key, op (gte|lte|eq|in), value clauses.")] List<SpecClauseArg>? specs = null,
        [Description("Sort: price_asc, price_desc, newest, name_asc, or name_desc.")] string? sort = null,
        [Description("One-based page number.")] int page = 1,
        [Description("Page size, 1 through 50.")] int pageSize = 20,
        CancellationToken ct = default)
    {
        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().ToLowerInvariant();
        var encodedSpecs = EncodeSpecs(specs);
        var args = new ProductQueryParams(
            query, category, brand, minPrice, maxPrice, inStockOnly, normalizedSort,
            page, pageSize, IncludeInactive: false, Specs: encodedSpecs);

        return await executor.RunAsync(
            "search_products",
            new { query, category, brand, minPrice, maxPrice, inStockOnly, specs, sort = normalizedSort, page, pageSize },
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

    [McpServerTool(Name = "compare_products", Title = "Compare products")]
    [Description("Compare 2 to 4 active products side by side: price, brand, category, stock, and specification rows.")]
    public static async Task<CallToolResult> CompareProducts(
        ToolExecutor executor,
        IProductComparisonBuilder comparisons,
        [Description("Between 2 and 4 product UUIDs.")] Guid[] productIds,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "compare_products",
            new { productIds },
            async token =>
            {
                if (productIds is null || productIds.Length == 0)
                    throw new DomainValidationException("productIds is required.");
                return (object?)await comparisons.BuildAsync(productIds, token);
            },
            ct);
    }

    [McpServerTool(Name = "find_products_for_requirement", Title = "Find products for a requirement")]
    [Description("Turn a shopping requirement into structured filters (budget, category, facet-supported use-case specs), search the catalog, and return ranked matches with the filters that produced them plus any questions to ask when the requirement is fuzzy. Deterministic, not an LLM.")]
    public static async Task<CallToolResult> FindProductsForRequirement(
        ToolExecutor executor,
        IRequirementSearchService requirements,
        [Description("The shopper's requirement in their own words, for example \"laptop for backend dev under $1200\".")] string requirementText,
        [Description("Optional price cap in USD; overrides a budget found in the text.")] decimal? budget = null,
        [Description("Optional category slug, for example laptops.")] string? categoryHint = null,
        CancellationToken ct = default)
    {
        return await executor.RunAsync(
            "find_products_for_requirement",
            new { requirementText, budget, categoryHint },
            async token =>
            {
                if (string.IsNullOrWhiteSpace(requirementText))
                    throw new DomainValidationException("requirementText is required.");
                if (budget is <= 0)
                    throw new DomainValidationException("budget must be greater than zero.");
                if (categoryHint is { Length: > 100 })
                    throw new DomainValidationException("categoryHint must be 100 characters or fewer.");

                return (object?)await requirements.SearchAsync(
                    new RequirementSearchRequest(requirementText, budget, categoryHint),
                    token);
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

    private static string? EncodeSpecs(IReadOnlyList<SpecClauseArg>? specs)
    {
        if (specs is null || specs.Count == 0) return null;

        var parts = new List<string>(specs.Count);
        foreach (var clause in specs)
        {
            if (string.IsNullOrWhiteSpace(clause.Key) || string.IsNullOrWhiteSpace(clause.Op))
                throw new InvalidSpecFilterException("specs clauses need a key and an op.");

            var value = clause.ValueText();
            if (value.Contains(';'))
                throw new InvalidSpecFilterException($"spec \"{clause.Key}\" value cannot contain ';'.");

            parts.Add($"{clause.Key.Trim()}:{clause.Op.Trim()}:{value}");
        }

        return string.Join(';', parts);
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
