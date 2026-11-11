namespace Shop.Application.Ai;

/// <summary>
/// A4: one provider-neutral conversation turn. The closed hierarchy encodes
/// what the wire protocol allows — a tool result always carries its call id,
/// and an assistant turn carries text, tool calls, or both.
/// </summary>
public abstract record AiMessage;
