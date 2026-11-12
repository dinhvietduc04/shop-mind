using System.Globalization;
using Shop.Domain.Exceptions;

namespace Shop.Application.Specs;

/// <summary>
/// A3 / §B5+ 4: the one fail-closed spec-filter codec. Any unregistered key,
/// unknown/invalid operator, or non-coercible value is an error — never a
/// silently ignored clause.
/// </summary>
public static class SpecFilterCodec
{
    public static IReadOnlyList<SpecClause> Parse(string? raw)
    {
        var clauses = new List<SpecClause>();
        if (string.IsNullOrWhiteSpace(raw)) return clauses;

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var first = part.IndexOf(':');
            var second = first < 0 ? -1 : part.IndexOf(':', first + 1);
            if (first <= 0 || second <= first)
                throw Invalid($"clause \"{part}\" is not key:op:value");

            var key = part[..first];
            var opText = part[(first + 1)..second];
            var valueText = part[(second + 1)..];

            var def = SpecRegistry.Find(key) ?? throw Invalid($"\"{key}\" is not a registered spec");
            var op = ParseOp(opText) ?? throw Invalid($"op \"{opText}\" is not allowed for {def.Type} spec \"{key}\"");
            if (!IsAllowed(def.Type, op))
                throw Invalid($"op \"{opText}\" is not allowed for {def.Type} spec \"{key}\"");
            if (valueText.Length == 0)
                throw Invalid($"\"{key}\" has no value");

            clauses.Add(new SpecClause(def.Key, op, ParseValue(def, op, valueText)));
        }

        return clauses;
    }

    /// <summary>
    /// A3: the inverse of <see cref="Parse"/> — the MCP/REST wire grammar. Used by
    /// composed services (find_products_for_requirement) that reuse the single
    /// filtered-search path instead of growing a second one.
    /// </summary>
    public static string Format(IReadOnlyList<SpecClause> clauses) =>
        string.Join(';', clauses.Select(FormatClause));

    private static string FormatClause(SpecClause clause) =>
        $"{clause.Key}:{OpText(clause.Op)}:{ValueText(clause)}";

    private static string OpText(SpecOp op) => op switch
    {
        SpecOp.Gte => "gte",
        SpecOp.Lte => "lte",
        SpecOp.Eq => "eq",
        SpecOp.In => "in",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "unknown spec op"),
    };

    private static string ValueText(SpecClause clause) => clause.Value switch
    {
        double n => n.ToString("0.############", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IReadOnlyList<string> values => string.Join('|', values),
        _ => Convert.ToString(clause.Value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static object ParseValue(SpecDefinition def, SpecOp op, string raw)
    {
        if (op == SpecOp.In)
        {
            var values = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 0) throw Invalid($"\"{def.Key}\" has no value");
            return values;
        }

        return def.Type switch
        {
            SpecType.Range when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                                && double.IsFinite(n) => n,
            SpecType.Range => throw Invalid($"\"{def.Key}\" needs a number, got \"{raw}\""),
            SpecType.Bool when raw is "true" or "false" => raw == "true",
            SpecType.Bool => throw Invalid($"\"{def.Key}\" needs true/false, got \"{raw}\""),
            _ => raw,
        };
    }

    private static SpecOp? ParseOp(string raw) => raw switch
    {
        "gte" => SpecOp.Gte,
        "lte" => SpecOp.Lte,
        "eq" => SpecOp.Eq,
        "in" => SpecOp.In,
        _ => null,
    };

    private static bool IsAllowed(SpecType type, SpecOp op) => type switch
    {
        SpecType.Range => op is SpecOp.Gte or SpecOp.Lte or SpecOp.Eq,
        SpecType.Enum => op is SpecOp.Eq or SpecOp.In,
        SpecType.Bool => op is SpecOp.Eq,
        _ => false,
    };

    private static InvalidSpecFilterException Invalid(string message) => new(message);
}
