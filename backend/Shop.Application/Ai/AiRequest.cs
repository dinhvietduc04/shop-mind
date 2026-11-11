namespace Shop.Application.Ai;

/// <summary>
/// A5 builds one of these per turn. <paramref name="Model"/> and
/// <paramref name="Temperature"/> are per-call overrides of the configured preset.
/// </summary>
public sealed record AiRequest(
    IReadOnlyList<AiMessage> Messages,
    IReadOnlyList<AiToolDefinition>? Tools = null,
    string? Model = null,
    int MaxTokens = 1024,
    double? Temperature = null);
