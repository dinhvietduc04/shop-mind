namespace Shop.Application.Ai;

public sealed record AiToolResultMessage(string CallId, string Content) : AiMessage;
