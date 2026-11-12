using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Requirements;
using Shop.Application.Specs;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

/// <summary>
/// M13 / ADR-0007: composes the A3 pieces into one tool call — resolve the
/// category, discover its facets, apply only facet-supported use-case hints,
/// search through the single filtered-search path, then rank deterministically.
/// The result explains itself with the filters it actually applied.
/// </summary>
public class RequirementSearchService(
    IProductService products,
    ICategoryService categories) : IRequirementSearchService
{
    private const int MaxRequirementLength = 500;
    private const int CandidatePool = 20; // enough to rank, bounded like search_products
    private const int DefaultLimit = 10;

    public async Task<RequirementSearchResult> SearchAsync(
        RequirementSearchRequest request, CancellationToken ct = default)
    {
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new DomainValidationException("requirementText is required.");
        if (text.Length > MaxRequirementLength)
            throw new DomainValidationException(
                $"requirementText must be {MaxRequirementLength} characters or fewer.");

        var budget = RequirementExtractor.Budget(text, request.Budget);
        var allCategories = await categories.ListPublicAsync(ct);
        var category = RequirementExtractor.Category(text, request.CategoryHint, allCategories);
        if (!string.IsNullOrWhiteSpace(request.CategoryHint) && category is null)
            throw new DomainValidationException(
                $"categoryHint \"{request.CategoryHint}\" is not a known category.");

        var facets = await products.GetFacetsAsync(category?.Slug, ct);

        // A3: hints are only used when the facet data can support them. With no
        // usable hint the request stays fuzzy, so the agent gets questions
        // instead of invented filters.
        var hints = ElicitationPolicy.UseCaseClauses(text, facets);
        var plan = hints.Count > 0
            ? ElicitationPolicy.BuildPlan(text, facets, hints)
            : ElicitationPolicy.BuildPlan(text, facets);

        var page = await products.QueryAsync(
            new ProductQueryParams(
                Search: null,
                Category: category?.Slug,
                Brand: null,
                MinPrice: null,
                MaxPrice: budget,
                InStockOnly: null,
                Sort: "price_asc",
                Page: 1,
                PageSize: CandidatePool,
                IncludeInactive: false,
                Specs: plan.Filters.Count == 0 ? null : SpecFilterCodec.Format(plan.Filters)),
            ct);

        var limit = Math.Clamp(request.Limit <= 0 ? DefaultLimit : request.Limit, 1, CandidatePool);
        var byId = page.Items.ToDictionary(p => p.Id);
        var ranked = SpecRanking.Rank(
            page.Items.Select(p => new RankableProduct(p.Id, p.Price, p.AvailableStock > 0, p.Specs)),
            plan.Filters);

        var applied = new List<string>();
        if (category is not null) applied.Add($"Category: {category.Name}");
        if (budget.HasValue) applied.Add($"Price <= {budget.Value:0.##} USD");
        applied.AddRange(plan.Explanation);

        return new RequirementSearchResult(
            text,
            category?.Slug,
            budget,
            applied,
            plan.Questions,
            ranked.Take(limit).Select(p => ToMatch(byId[p.Id], plan.Filters, budget)).ToList(),
            page.TotalCount);
    }

    private static RequirementMatchDto ToMatch(
        ProductDto product, IReadOnlyList<SpecClause> filters, decimal? budget)
    {
        var why = new List<string>();
        if (product.AvailableStock > 0) why.Add("in stock");

        foreach (var clause in filters)
            if (SpecMatcher.Matches(product.Specs, clause))
                why.Add(ElicitationPolicy.Describe(clause));

        if (budget.HasValue) why.Add("within budget");

        return new RequirementMatchDto(
            product.Id,
            product.Name,
            product.Slug,
            product.SKU,
            product.Brand,
            product.CategoryName,
            product.Price,
            product.AvailableStock,
            product.AvailableStock > 0,
            why);
    }
}
