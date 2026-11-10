namespace Shop.Domain.Entities;

/// <summary>
/// B4 analytics consumer sink (M18 schema, pre-cut). Collected for ops — the
/// personalization engine itself stays backlog per AI_ASSISTANT_PLAN §9.
/// </summary>
public class UserBehaviorEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? QueryText { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
