using System.Globalization;
using System.Text.RegularExpressions;
using Shop.Application.Dtos;

namespace Shop.Application.Requirements;

/// <summary>
/// M13 / ADR-0007: deterministic requirement extraction. No model, no
/// embeddings — a budget phrase, a category word, and the A3 use-case hint
/// table are the whole vocabulary. Unknown words are ignored, never invented
/// into filters.
/// </summary>
public static class RequirementExtractor
{
    private const decimal MinimumBareBudget = 50m; // "under 5 hours" is not a price

    // Irregular plurals the plain "strip the s" rule cannot cover.
    private static readonly IReadOnlyDictionary<string, string[]> CategoryAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["smartphones"] = ["smartphone", "phone", "phones", "mobile", "mobiles"],
            ["mice"] = ["mouse"],
            ["accessories"] = ["accessory"],
            ["components"] = ["component", "gpu", "graphics card"],
        };

    private static readonly Regex BudgetPattern = new(
        @"(?:
            (?<budgetWord>under|below|less\s+than|max(?:imum)?|up\s+to|at\s+most|budget(?:\s+of|\s+is)?)
            \s*
            (?<currency1>\$|usd\s*)?
          | (?<currency2>\$|usd\s*)
        )
        (?<amount>\d[\d,]*(?:\.\d{1,2})?)
        (?<k>\s*k\b)?",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant);

    /// <summary>Explicit budget wins; otherwise the first price-shaped phrase in the text.</summary>
    public static decimal? Budget(string text, decimal? explicitBudget = null)
    {
        if (explicitBudget is > 0) return explicitBudget;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = BudgetPattern.Match(text);
        if (!match.Success) return null;

        var raw = match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return null;

        if (match.Groups["k"].Success) amount *= 1_000;
        if (!double.IsFinite(amount) || amount <= 0) return null;

        var hasCurrency = match.Groups["currency1"].Success || match.Groups["currency2"].Success;
        if (!hasCurrency && amount < (double)MinimumBareBudget) return null;

        return decimal.Round((decimal)amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A hint must resolve to a known category (else null, so the caller can fail
    /// closed); without a hint the longest matching word in the text wins.
    /// </summary>
    public static CategoryDto? Category(
        string text, string? categoryHint, IReadOnlyList<CategoryDto> categories)
    {
        var active = categories.Where(c => c.IsActive).ToList();

        if (!string.IsNullOrWhiteSpace(categoryHint))
        {
            var hint = categoryHint.Trim();
            return active.FirstOrDefault(c => Eq(c.Slug, hint))
                ?? active.FirstOrDefault(c => Eq(c.Name, hint));
        }

        return MatchInText(text, active);
    }

    private static CategoryDto? MatchInText(string text, IReadOnlyList<CategoryDto> categories)
    {
        CategoryDto? best = null;
        var bestLength = 0;

        foreach (var category in categories)
        {
            foreach (var word in Words(category))
            {
                if (word.Length <= bestLength) continue;
                if (!ContainsWord(text, word)) continue;
                best = category;
                bestLength = word.Length;
            }
        }

        return best;
    }

    private static IEnumerable<string> Words(CategoryDto category)
    {
        var slug = category.Slug.ToLowerInvariant();
        var name = category.Name.ToLowerInvariant();

        yield return slug;
        yield return name;

        var singularSlug = Singular(slug);
        if (!string.Equals(singularSlug, slug, StringComparison.Ordinal)) yield return singularSlug;

        var singularName = Singular(name);
        if (!string.Equals(singularName, name, StringComparison.Ordinal)) yield return singularName;

        if (CategoryAliases.TryGetValue(category.Slug, out var aliases))
            foreach (var alias in aliases) yield return alias;
    }

    private static string Singular(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
            ? word[..^1]
            : word;

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));

    private static bool Eq(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
