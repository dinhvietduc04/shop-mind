namespace Shop.Domain.Enums;

/// <summary>
/// A6: the auditable action the agent attempted. Assist currently has one kind:
/// creating a quote for the human to review and pay. Delegated adds the
/// spend-moving kinds later through the same table.
/// </summary>
public enum AiActionType
{
    CreateQuote = 0,
}
