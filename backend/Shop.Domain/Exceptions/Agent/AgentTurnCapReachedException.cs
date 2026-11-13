namespace Shop.Domain.Exceptions;

/// <summary>
/// A5: the per-conversation turn budget is a server fact, not a UI convention.
/// Reaching it is a client-visible conflict with a typed code so the UI can
/// offer "start a new conversation" instead of retrying forever.
/// </summary>
public sealed class AgentTurnCapReachedException : ShopDomainException
{
    public int TurnCap { get; }
    public int TurnsUsed { get; }

    public AgentTurnCapReachedException(int turnCap, int turnsUsed)
        : base("TURN_CAP_REACHED",
            $"This conversation reached its {turnCap}-turn limit ({turnsUsed} used). Start a new conversation.")
    {
        TurnCap = turnCap;
        TurnsUsed = turnsUsed;
    }
}
