namespace Shop.Domain.Enums;

/// <summary>M10: outbox row lifecycle for the M11 publisher.</summary>
public enum OutboxStatus : short
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}
