namespace Shop.Application.Ai;

/// <summary>
/// One tool exposed to the model. The parameter schema is the MCP tool's JSON
/// schema verbatim, so the manifest stays the single source of truth.
/// </summary>
public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);
