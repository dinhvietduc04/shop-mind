namespace Shop.Domain.Exceptions;

/// <summary>
/// A5: the turn's wall-clock budget (Agent:MaxTurnSeconds) elapsed. Surfaced as
/// a typed code so the SSE error frame can tell the UI to retry rather than
/// treat the turn as a bug.
/// </summary>
public sealed class AgentTurnTimeoutException : ShopDomainException
{
    public AgentTurnTimeoutException(int seconds)
        : base("TURN_TIMEOUT",
            $"The assistant exceeded its {seconds}-second turn budget. Please retry with a narrower request.") { }
}
