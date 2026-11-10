using System.Globalization;
using Shop.Application.Dtos;

namespace Shop.Application.Specs;

/// <summary>
/// A3 / ADR-0007: deterministic elicitation, explicitly not a recommendation
/// engine. Fuzzy requests get facet discovery and at most three attribute
/// questions; explicit requirements always survive; inferred use-case hints
/// are added only after facet discovery and only when the facet supports them.
/// </summary>
public static class ElicitationPolicy
{
    private const int MaxQuestions = 3;

    /// <summary>The use-case mapping table (A3's <c>Agent:UseCaseHints</c> seam).</summary>
    private static readonly (string[] Phrases, SpecClause[] Clauses)[] Hints =
    [
        (["backend dev", "backend", "developer", "programming", "coding"],
            [new SpecClause("ram_gb", SpecOp.Gte, 16d)]),
        (["video editing", "editing", "rendering"],
            [new SpecClause("ram_gb", SpecOp.Gte, 32d)]),
        (["gaming", "game"],
            [new SpecClause("vram_gb", SpecOp.Gte, 12d)]),
    ];

    public static ElicitationPlan BuildPlan(
        string message,
        IReadOnlyList<FacetDto>? facets = null,
        IReadOnlyList<SpecClause>? explicitRequirements = null,
        int maxQuestions = MaxQuestions)
    {
        var available = facets ?? [];
        var explicitClauses = explicitRequirements ?? [];

        if (explicitClauses.Count > 0)
        {
            var filters = new List<SpecClause>(explicitClauses);
            foreach (var hint in HintsFor(message, available))
            {
                if (filters.All(f => !string.Equals(f.Key, hint.Key, StringComparison.OrdinalIgnoreCase)))
                    filters.Add(hint);
            }

            return new ElicitationPlan(false, [], filters, filters.Select(Describe).ToList());
        }

        // Fuzzy request: discovery comes before any filter.
        if (available.Count == 0)
            return new ElicitationPlan(true, [], [], []);

        var questions = available
            .Where(f => f.Type is "range" or "enum")
            .OrderBy(f => f.Type == "range" ? 0 : 1)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(Math.Clamp(maxQuestions, 0, MaxQuestions))
            .Select(f => new ElicitationQuestion(f.Key, f.Label, f.Type, f.Unit, f.Values, f.Min, f.Max))
            .ToList();

        return new ElicitationPlan(false, questions, [], []);
    }

    private static IEnumerable<SpecClause> HintsFor(string message, IReadOnlyList<FacetDto> facets)
    {
        var text = message.ToLowerInvariant();
        foreach (var (phrases, clauses) in Hints)
        {
            if (!phrases.Any(text.Contains)) continue;
            foreach (var clause in clauses)
            {
                var facet = facets.FirstOrDefault(f => string.Equals(f.Key, clause.Key, StringComparison.OrdinalIgnoreCase));
                if (facet is null || !Supported(facet, clause)) continue;
                yield return clause;
            }
        }
    }

    private static bool Supported(FacetDto facet, SpecClause clause) => clause switch
    {
        { Value: double n } => facet.Min.HasValue && facet.Max.HasValue
                               && n >= facet.Min.Value && n <= facet.Max.Value,
        { Op: SpecOp.Eq, Value: string s } => facet.Values is { Count: > 0 } values
                                              && values.Contains(s, StringComparer.OrdinalIgnoreCase),
        { Op: SpecOp.In, Value: IReadOnlyList<string> values } => facet.Values is { Count: > 0 } options
                                                                  && values.Count > 0
                                                                  && values.All(v => options.Contains(v, StringComparer.OrdinalIgnoreCase)),
        _ => false,
    };

    private static string Describe(SpecClause clause)
    {
        var def = SpecRegistry.Find(clause.Key);
        var label = def?.Label ?? clause.Key;
        var unit = string.IsNullOrWhiteSpace(def?.Unit) ? string.Empty : $" {def.Unit}";

        return clause.Op switch
        {
            SpecOp.Gte => $"{label} >= {Format(clause.Value)}{unit}",
            SpecOp.Lte => $"{label} <= {Format(clause.Value)}{unit}",
            SpecOp.In => $"{label} in [{string.Join(", ", (IReadOnlyList<string>)clause.Value)}]",
            _ => $"{label} = {Format(clause.Value)}{unit}",
        };
    }

    private static string Format(object value) => value switch
    {
        double n => n.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? string.Empty,
    };
}
