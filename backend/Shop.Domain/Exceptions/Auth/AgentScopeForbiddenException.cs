namespace Shop.Domain.Exceptions;

/// <summary>
/// B5 (ADR-0006): an agent-scope token may never touch money REST —
/// quote creation, quote reads, and pay confirmation are human-only surfaces.
/// </summary>
public class AgentScopeForbiddenException : ShopDomainException
{
    public AgentScopeForbiddenException(string what)
        : base("AgentScopeForbidden", $"Agent-scope tokens cannot perform: {what}.") { }
}
