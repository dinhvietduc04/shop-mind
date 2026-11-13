namespace Shop.Application.Agent;

/// <summary>A5: the only chat input. Omitting the id starts a new conversation.</summary>
public sealed record AgentChatRequest(string Message, Guid? ConversationId = null);
