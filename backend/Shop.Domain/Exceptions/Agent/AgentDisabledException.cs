namespace Shop.Domain.Exceptions;

/// <summary>
/// A5: the agent kill switch (<c>Agent:Enabled=false</c>) is checked before any
/// model or tool work. Non-AI routes never depend on it.
/// </summary>
public sealed class AgentDisabledException : ShopDomainException
{
    public AgentDisabledException()
        : base("AGENT_DISABLED", "The shopping assistant is currently disabled.") { }
}
