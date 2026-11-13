using Shop.Domain.Enums;

namespace Shop.Domain.Entities;

/// <summary>
/// A6: the audit row for an agent action and the origin tag for a quote the
/// agent created. Assist quotes do not need a confirm-to-execute step — the
/// quote itself is the human-review artifact — so the row is written Executed
/// on success and Rejected when policy, budget, or a human-held quote blocks
/// it. The sweeper flips an Executed row to Expired when its quote dies unpaid.
/// </summary>
public class AiAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
    public AiActionType Type { get; set; }
    public AiActionStatus Status { get; set; }
    public QuoteOrigin Origin { get; set; } = QuoteOrigin.Agent;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }

    public User? User { get; set; }
    public Order? Order { get; set; }
}
