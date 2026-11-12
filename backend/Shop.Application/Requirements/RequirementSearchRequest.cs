namespace Shop.Application.Requirements;

/// <summary>M13: the agent's natural-language requirement plus optional hard hints.</summary>
public sealed record RequirementSearchRequest(
    string Text,
    decimal? Budget = null,
    string? CategoryHint = null,
    int Limit = 10);
