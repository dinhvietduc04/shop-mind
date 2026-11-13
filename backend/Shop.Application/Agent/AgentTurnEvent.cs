namespace Shop.Application.Agent;

/// <summary>
/// A5 SSE contract (B5+ #3): only three frame types exist. The controller
/// serializes these; the loop decides when a delta or the final turn is ready.
/// </summary>
public abstract record AgentTurnEvent;

public sealed record AgentDeltaEvent(string Text) : AgentTurnEvent;

public sealed record AgentTurnCompletedEvent(AgentTurnResult Result) : AgentTurnEvent;
