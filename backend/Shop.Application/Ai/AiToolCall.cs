namespace Shop.Application.Ai;

/// <summary>
/// A tool invocation requested by the model. The arguments stay raw JSON here:
/// the MCP tool schema validates them at the tool boundary, not in the provider.
/// </summary>
public sealed record AiToolCall(string CallId, string Name, string ArgumentsJson);
