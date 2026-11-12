using Shop.Application.Dtos;
using Shop.Application.Requirements;

namespace Shop.UnitTests;

/// <summary>
/// M13 / ADR-0007: requirement extraction is deterministic and fail-closed. It
/// may only turn words that are provably budget/category shaped into filters;
/// everything else is ignored rather than guessed.
/// </summary>
public class RequirementExtractorTests
{
    private static readonly IReadOnlyList<CategoryDto> Categories =
    [
        Category("laptops", "Laptops"),
        Category("smartphones", "Smartphones"),
        Category("mice", "Mice"),
        Category("accessories", "Accessories"),
        Category("archived", "Archived", isActive: false),
    ];

    [Theory]
    [InlineData("laptop for backend dev under $1200", 1200)]
    [InlineData("I need a phone below USD 999 please", 999)]
    [InlineData("gaming rig up to $1.2k", 1200)]
    [InlineData("budget of 1,299.50 dollars", 1299.50)]
    [InlineData("max 750", 750)]
    public void Budget_reads_price_shaped_phrases(string text, double expected)
    {
        Assert.Equal((decimal)expected, RequirementExtractor.Budget(text));
    }

    [Theory]
    [InlineData("under 5 hours")]
    [InlineData("no price in this sentence")]
    [InlineData("")]
    public void Budget_ignores_phrases_that_are_not_prices(string text)
    {
        Assert.Null(RequirementExtractor.Budget(text));
    }

    [Fact]
    public void An_explicit_budget_overrides_the_text()
    {
        Assert.Equal(900m, RequirementExtractor.Budget("laptop under $1200", 900m));
    }

    [Theory]
    [InlineData("laptop for backend dev", "laptops")]
    [InlineData("wireless mouse for work", "mice")]
    [InlineData("a phone with a good camera", "smartphones")]
    [InlineData("laptop accessories", "accessories")]
    public void Category_matches_the_longest_category_word_in_the_text(
        string text, string expectedSlug)
    {
        Assert.Equal(expectedSlug, RequirementExtractor.Category(text, null, Categories)?.Slug);
    }

    [Fact]
    public void A_category_hint_may_name_the_slug_or_the_name()
    {
        Assert.Equal("laptops", RequirementExtractor.Category("anything", "laptops", Categories)?.Slug);
        Assert.Equal("laptops", RequirementExtractor.Category("anything", "Laptops", Categories)?.Slug);
    }

    [Fact]
    public void An_unknown_hint_fails_closed_instead_of_guessing_from_the_text()
    {
        Assert.Null(RequirementExtractor.Category("laptop", "toasters", Categories));
    }

    [Fact]
    public void Inactive_categories_are_never_matched()
    {
        Assert.Null(RequirementExtractor.Category("archived thing", "archived", Categories));
    }

    private static CategoryDto Category(string slug, string name, bool isActive = true) =>
        new(Guid.NewGuid(), name, slug, null, isActive);
}
