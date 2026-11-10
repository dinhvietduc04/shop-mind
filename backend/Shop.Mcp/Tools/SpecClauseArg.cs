using System.Text.Json;
using Shop.Domain.Exceptions;

namespace Shop.Mcp.Tools;

/// <summary>
/// A2: one spec clause on the MCP wire, mirroring the REST grammar
/// <c>key:op:value</c>. Values bind as JSON so a number or bool is not forced
/// through a string before the registry validates it.
/// </summary>
public sealed record SpecClauseArg(string Key, string Op, JsonElement Value)
{
    public string ValueText() => Value.ValueKind switch
    {
        JsonValueKind.String => Value.GetString() ?? string.Empty,
        JsonValueKind.Number => Value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new InvalidSpecFilterException($"spec \"{Key}\" has an unsupported value."),
    };
}
