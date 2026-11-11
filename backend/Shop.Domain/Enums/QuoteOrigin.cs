namespace Shop.Domain.Enums;

/// <summary>
/// A2 / ADR-0006 Glossary "Origin": who asked for a quote. Agent-origin open
/// quotes are replaceable by the agent; a Human-origin open quote blocks the
/// agent with QUOTE_HELD_BY_HUMAN so it can never transitively cancel human
/// money state. Humans may replace either origin.
/// </summary>
public enum QuoteOrigin
{
    Human = 0,
    Agent = 1,
}
