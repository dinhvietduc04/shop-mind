using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.Extensions;
using Shop.Application.Agent;

namespace Shop.Api.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize]
public sealed class AgentController(AgentLoop loop, ILogger<AgentController> logger) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<ActionResult> Chat([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(AgentChatRequest.Message)] = ["Message is required."]
            }));

        if (!User.Identity?.IsAuthenticated ?? true)
        {
            var loginRequired = RequiresLogin(request.Message);
            return loginRequired
                ? Unauthorized(new { code = "LOGIN_REQUIRED", message = "Sign in to use the shopping assistant.", requiresLogin = true })
                : Ok(new { code = "LOGIN_REQUIRED", message = "Sign in to use the shopping assistant for a full comparison and cart flow.", requiresLogin = true });
        }

        var userId = User.GetUserId();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";
        var correlationId = HttpContext.TraceIdentifier;

        var result = await loop.RunAsync(userId, role, request, correlationId, _ => Task.CompletedTask, ct);

        return Ok(new
        {
            conversationId = result.ConversationId,
            replyText = result.ReplyText,
            toolCallsSummary = result.ToolCalls.Select(t => new
            {
                name = t.Name,
                status = t.Status,
                attempts = t.Attempts,
                durationMs = t.DurationMs,
                errorCode = t.ErrorCode,
            }),
            pendingAction = result.PendingQuote is null ? null : new
            {
                orderId = result.PendingQuote.OrderId,
                hostedPayUrl = result.PendingQuote.HostedPayUrl,
                expiresAt = result.PendingQuote.ExpiresAt,
                totalAmount = result.PendingQuote.TotalAmount,
            },
            turnCap = result.TurnCap,
            turnsUsed = result.TurnsUsed,
            limitReached = result.LimitReached,
            usage = new
            {
                inputTokens = result.Usage.InputTokens,
                outputTokens = result.Usage.OutputTokens,
            },
        });
    }

    private static bool RequiresLogin(string message)
    {
        var lower = message.Trim();
        if (lower.Length == 0)
            return true;

        var actionWords = new[]
        {
            "add", "buy", "cart", "checkout", "purchase", "quote", "order", "remove", "reserve",
        };

        return actionWords.Any(word => lower.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
