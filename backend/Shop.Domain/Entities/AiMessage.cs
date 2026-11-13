using Shop.Domain.Enums;

namespace Shop.Domain.Entities;

/// <summary>
/// A5: one persisted provider-neutral message. <see cref="TurnNumber"/> groups
/// every assistant/tool message of a user turn so history trimming can drop
/// whole turns and never split a tool call from its result.
/// </summary>
public class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public int TurnNumber { get; set; }
    public int Ordinal { get; set; }
    public AiMessageRole Role { get; set; }
    public string? Content { get; set; }
    public string? ToolCallsJson { get; set; }
    public string? ToolCallId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AiConversation? Conversation { get; set; }
}
