namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: one tool-to-risk mapping. <see cref="AllowAnonymous"/> is true
/// only for the public catalog read tools; everything else requires a valid
/// agent JWT.
/// </summary>
public sealed record ToolPolicy(
    string Name,
    ToolRiskClass RiskClass,
    bool AllowAnonymous = false);
