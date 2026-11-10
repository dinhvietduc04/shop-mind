using System.Text.Json;

namespace Shop.Application.Specs;

/// <summary>A3: in-memory evaluator shared by ranking and the agent's explanations.</summary>
public static class SpecMatcher
{
    public static bool Matches(JsonElement specs, SpecClause clause)
    {
        if (!specs.TryGetProperty(clause.Key, out var value)) return false;

        return clause switch
        {
            { Op: SpecOp.Gte, Value: double n } =>
                value.ValueKind == JsonValueKind.Number && value.GetDouble() >= n,
            { Op: SpecOp.Lte, Value: double n } =>
                value.ValueKind == JsonValueKind.Number && value.GetDouble() <= n,
            { Op: SpecOp.Eq, Value: double n } =>
                value.ValueKind == JsonValueKind.Number && value.GetDouble() == n,
            { Op: SpecOp.Eq, Value: bool b } =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean() == b,
            { Op: SpecOp.Eq, Value: string s } =>
                value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), s, StringComparison.OrdinalIgnoreCase),
            { Op: SpecOp.In, Value: IReadOnlyList<string> values } =>
                value.ValueKind == JsonValueKind.String
                && values.Contains(value.GetString()!, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
