namespace Shop.Application.Ai;

public sealed record AiAssistantMessage(string? Text, IReadOnlyList<AiToolCall> ToolCalls) : AiMessage;
