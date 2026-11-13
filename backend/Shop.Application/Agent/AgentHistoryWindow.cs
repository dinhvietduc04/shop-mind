using Shop.Application.Ai;
using Shop.Domain.Enums;
using AiMessageEntity = Shop.Domain.Entities.AiMessage;

namespace Shop.Application.Agent;

/// <summary>
/// A5: the "system + last N turns" window. A turn is one user message plus every
/// assistant/tool message produced while answering it, so history is trimmed
/// whole-turn from the oldest end — a tool call and its matching result can
/// never be split, and the stable system prefix is never rewritten.
/// </summary>
public static class AgentHistoryWindow
{
    /// <summary>Cheap, deterministic token estimate used only to bound the window.</summary>
    public const int CharsPerToken = 4;
    private const int MessageEnvelopeOverheadChars = 16;

    public static IReadOnlyList<AiMessage> Build(
        IReadOnlyList<AiMessageEntity> persisted,
        int historyTurns,
        int maxPromptTokens)
    {
        if (persisted.Count == 0)
            return [];

        var turns = persisted
            .GroupBy(m => m.TurnNumber)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(m => m.Ordinal).ToList())
            .ToList();

        var budgetTurns = Math.Max(1, historyTurns);
        if (turns.Count > budgetTurns)
            turns = turns[^budgetTurns..];

        var tokenLimit = Math.Max(1, maxPromptTokens);
        while (turns.Count > 1 && EstimateTokens(turns) > tokenLimit)
            turns.RemoveAt(0);

        return turns.SelectMany(MapTurn).ToList();
    }

    private static IEnumerable<AiMessage> MapTurn(IReadOnlyList<AiMessageEntity> turn)
    {
        foreach (var message in turn)
        {
            switch (message.Role)
            {
                case AiMessageRole.User:
                    yield return new AiUserMessage(message.Content ?? string.Empty);
                    break;

                case AiMessageRole.Assistant:
                    yield return new AiAssistantMessage(
                        message.Content,
                        AgentSystemPrompt.DeserializeToolCalls(message.ToolCallsJson));
                    break;

                case AiMessageRole.Tool when message.ToolCallId is not null:
                    yield return new AiToolResultMessage(
                        message.ToolCallId,
                        message.Content ?? string.Empty);
                    break;
            }
        }
    }

    private static int EstimateTokens(IReadOnlyList<List<AiMessageEntity>> turns)
    {
        var chars = 0;
        foreach (var message in turns.SelectMany(t => t))
        {
            chars += message.Content?.Length ?? 0;
            chars += message.ToolCallsJson?.Length ?? 0;
            chars += MessageEnvelopeOverheadChars; // role / envelope overhead
        }
        return chars / CharsPerToken;
    }
}
