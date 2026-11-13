namespace Shop.Domain.Enums;

/// <summary>
/// A5: the persisted role of one conversation turn. Tool calls and their
/// matching results are stored as separate rows so the whole-turn window can
/// be replayed without ever breaking tool-call/result pairing.
/// </summary>
public enum AiMessageRole
{
    User = 0,
    Assistant = 1,
    Tool = 2,
}
