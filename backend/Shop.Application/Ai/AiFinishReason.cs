namespace Shop.Application.Ai;

/// <summary>
/// Normalized stop reason. <see cref="Unknown"/> keeps a provider-specific value
/// from aborting the turn; callers decide whether an unknown reason is safe.
/// </summary>
public enum AiFinishReason
{
    Stop,
    Length,
    ToolCalls,
    ContentFilter,
    Unknown,
}
