namespace Shop.Domain.Entities;

/// <summary>
/// A5: one chat thread owned by one user. <see cref="TurnsUsed"/> is the
/// server-enforced counter behind <c>turnCap</c>/<c>turnsUsed</c>; chat text is
/// never exposed to admins and is purged by the retention sweeper after 30 days.
/// </summary>
public class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TurnsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}
