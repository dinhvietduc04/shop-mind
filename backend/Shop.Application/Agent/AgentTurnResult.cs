using Shop.Application.Ai;

namespace Shop.Application.Agent;

/// <summary>
/// A5/B5+ contract #3: the <c>turn</c> frame payload. <see cref="TurnCap"/> and
/// <see cref="TurnsUsed"/> let the UI render "turn x/N" without hardcoding the
/// server budget; <see cref="PendingQuote"/> carries the human hand-off.
/// </summary>
public sealed record AgentTurnResult(
    Guid ConversationId,
    string ReplyText,
    IReadOnlyList<AgentToolCallTrace> ToolCalls,
    AgentQuoteHandoff? PendingQuote,
    int TurnCap,
    int TurnsUsed,
    bool LimitReached,
    AiUsage Usage);

/// <summary>One tool attempt in the turn, for the UI summary and tests.</summary>
public sealed record AgentToolCallTrace(
    string Name,
    string Status,
    int Attempts,
    double DurationMs,
    string? ErrorCode);

/// <summary>
/// A6 hand-off: the human opens <see cref="HostedPayUrl"/> in their own browser.
/// The agent never navigates or confirms it, and no payment state is implied.
/// </summary>
public sealed record AgentQuoteHandoff(
    Guid OrderId,
    string HostedPayUrl,
    DateTime ExpiresAt,
    decimal TotalAmount);
