namespace Shop.Application.Ai;

public sealed record AiUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
