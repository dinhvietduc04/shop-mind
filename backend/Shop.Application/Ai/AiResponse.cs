namespace Shop.Application.Ai;

/// <summary>
/// The normalized reply every provider returns. <paramref name="Text"/> is null
/// for a pure tool-call turn; <paramref name="ToolCalls"/> is empty for a text turn.
/// </summary>
public sealed record AiResponse(
    string? Text,
    IReadOnlyList<AiToolCall> ToolCalls,
    AiUsage Usage,
    string Model,
    TimeSpan Duration,
    AiFinishReason FinishReason);
