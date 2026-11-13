using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Application.Ai;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using AiMessage = Shop.Application.Ai.AiMessage;
using AiMessageEntity = Shop.Domain.Entities.AiMessage;

namespace Shop.Application.Agent;

/// <summary>
/// A5: one chat turn. The loop owns the budgets, the whole-turn history window,
/// tool dispatch through the MCP gateway, the AiMessages transcript, and the
/// AiActions audit row. It never opens the pay page and never moves money; the
/// only quote it can produce is a human-review artifact.
///
/// Turn lifecycle:
///   1. enforce kill switch + per-conversation turn cap
///   2. persist the user message and consume the turn (cost is spent on start)
///   3. mint a single-turn scope=agent token (never logged or returned)
///   4. call the provider, execute tool calls with bounded retries, persist
///      every assistant/tool message so the next turn replays a legal transcript
///   5. emit exactly one delta for the final text and one turn frame
/// </summary>
public sealed class AgentLoop(
    IAppDbContext db,
    IAiProvider ai,
    IAgentToolGateway gateway,
    IAgentTokenMinter tokens,
    IToolRiskPolicy toolPolicy,
    IOptionsMonitor<AgentOptions> options,
    ILogger<AgentLoop> logger)
{
    private const string QuoteToolName = "create_quote";
    private const string ToolTransportError = "TOOL_TRANSPORT_ERROR";
    private const string ToolError = "TOOL_ERROR";

    public async Task<AgentTurnResult> RunAsync(
        Guid userId,
        string role,
        AgentChatRequest request,
        string correlationId,
        Func<AgentTurnEvent, Task> emit,
        CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (!o.Enabled)
            throw new AgentDisabledException();

        var conversation = await LoadOrCreateConversationAsync(userId, request, ct);
        if (conversation.TurnsUsed >= o.MaxTurnsPerConversation)
            throw new AgentTurnCapReachedException(o.MaxTurnsPerConversation, conversation.TurnsUsed);

        var turnNumber = conversation.TurnsUsed + 1;
        conversation.TurnsUsed = turnNumber;
        conversation.UpdatedAt = DateTime.UtcNow;
        if (turnNumber == 1 && string.IsNullOrWhiteSpace(conversation.Title))
            conversation.Title = Truncate(request.Message.Trim(), 120);

        db.AiMessages.Add(new AiMessageEntity
        {
            ConversationId = conversation.Id,
            TurnNumber = turnNumber,
            Ordinal = 0,
            Role = AiMessageRole.User,
            Content = request.Message,
        });
        await db.SaveChangesAsync(ct);

        var transcript = await db.AiMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.TurnNumber)
            .ThenBy(m => m.Ordinal)
            .ToListAsync(ct);

        var messages = new List<AiMessage>
        {
            new AiSystemMessage(AgentSystemPrompt.Core),
        };
        var addendum = AgentSystemPrompt.DiscoveryAddendum(request.Message);
        if (addendum is not null)
            messages.Add(new AiSystemMessage(addendum));
        messages.AddRange(AgentHistoryWindow.Build(transcript, o.HistoryTurns, o.MaxPromptTokens));

        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        turnCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.MaxTurnSeconds)));
        var turnToken = turnCts.Token;

        // ADR-0006: the delegated token is handed only to the MCP client. It is
        // minted per turn, capped at 60s, and never persisted or serialized.
        var ttl = TimeSpan.FromSeconds(Math.Clamp(o.AgentTokenTtlSeconds, 1, 60));
        var agentToken = tokens.Mint(userId, role, ttl);

        await using var session = await gateway.OpenSessionAsync(
            userId, role, agentToken, correlationId, turnToken);

        var traces = new List<AgentToolCallTrace>();
        var usage = new AiUsage(0, 0);
        var ordinal = 1;
        var toolCalls = 0;
        var limitReached = false;
        AgentQuoteHandoff? pendingQuote = null;
        var replyText = string.Empty;

        try
        {
            while (true)
            {
                var response = await ai.GenerateAsync(
                    new AiRequest(
                        messages,
                        session.Tools,
                        MaxTokens: o.MaxOutputTokens,
                        Temperature: o.Temperature),
                    turnToken);

                usage = new AiUsage(
                    usage.InputTokens + response.Usage.InputTokens,
                    usage.OutputTokens + response.Usage.OutputTokens);

                if (response.ToolCalls.Count == 0)
                {
                    replyText = response.Text ?? string.Empty;
                    db.AiMessages.Add(new AiMessageEntity
                    {
                        ConversationId = conversation.Id,
                        TurnNumber = turnNumber,
                        Ordinal = ordinal,
                        Role = AiMessageRole.Assistant,
                        Content = replyText,
                    });
                    await db.SaveChangesAsync(ct);
                    break;
                }

                // Whole-turn budget: never accept more tool calls than remain.
                // The persisted assistant message carries only the calls that are
                // actually executed, so a later history replay never sees an
                // orphaned tool call.
                var remaining = Math.Max(0, o.MaxToolCallsPerTurn - toolCalls);
                if (remaining == 0)
                {
                    limitReached = true;
                    replyText = ToolBudgetReply(o.MaxToolCallsPerTurn);
                    db.AiMessages.Add(new AiMessageEntity
                    {
                        ConversationId = conversation.Id,
                        TurnNumber = turnNumber,
                        Ordinal = ordinal,
                        Role = AiMessageRole.Assistant,
                        Content = replyText,
                    });
                    await db.SaveChangesAsync(ct);
                    break;
                }

                var accepted = response.ToolCalls.Take(remaining).ToList();
                if (accepted.Count < response.ToolCalls.Count)
                    limitReached = true;

                var assistantMessage = new AiAssistantMessage(response.Text, accepted);
                messages.Add(assistantMessage);
                db.AiMessages.Add(new AiMessageEntity
                {
                    ConversationId = conversation.Id,
                    TurnNumber = turnNumber,
                    Ordinal = ordinal++,
                    Role = AiMessageRole.Assistant,
                    Content = response.Text,
                    ToolCallsJson = AgentSystemPrompt.SerializeToolCalls(accepted),
                });
                await db.SaveChangesAsync(ct);

                foreach (var call in accepted)
                {
                    toolCalls++;
                    var prepared = PrepareInvocation(call);
                    var (toolResult, attempts, durationMs) = await InvokeWithRetriesAsync(
                        session, prepared, o, turnToken);

                    traces.Add(new AgentToolCallTrace(
                        call.Name,
                        toolResult.IsError ? "error" : "ok",
                        attempts,
                        durationMs,
                        toolResult.ErrorCode));

                    messages.Add(new AiToolResultMessage(call.CallId, toolResult.Content));
                    db.AiMessages.Add(new AiMessageEntity
                    {
                        ConversationId = conversation.Id,
                        TurnNumber = turnNumber,
                        Ordinal = ordinal++,
                        Role = AiMessageRole.Tool,
                        Content = toolResult.Content,
                        ToolCallId = call.CallId,
                    });
                    await db.SaveChangesAsync(ct);

                    if (string.Equals(call.Name, QuoteToolName, StringComparison.Ordinal))
                    {
                        var handoff = toolResult.IsError ? null : TryReadHandoff(toolResult.Content);
                        if (handoff is not null)
                            pendingQuote = handoff;

                        await RecordQuoteActionAsync(
                            userId, conversation.Id, prepared.IdempotencyKey,
                            toolResult, handoff, ct);
                    }
                }

                if (limitReached)
                {
                    replyText = ToolBudgetReply(o.MaxToolCallsPerTurn);
                    db.AiMessages.Add(new AiMessageEntity
                    {
                        ConversationId = conversation.Id,
                        TurnNumber = turnNumber,
                        Ordinal = ordinal,
                        Role = AiMessageRole.Assistant,
                        Content = replyText,
                    });
                    await db.SaveChangesAsync(ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AgentTurnTimeoutException(o.MaxTurnSeconds);
        }

        if (replyText.Length > 0)
            await emit(new AgentDeltaEvent(replyText));

        var result = new AgentTurnResult(
            conversation.Id,
            replyText,
            traces,
            pendingQuote,
            o.MaxTurnsPerConversation,
            conversation.TurnsUsed,
            limitReached,
            usage);

        logger.LogInformation(
            "Agent turn {Turn}/{Cap} completed for conversation {ConversationId}: {ToolCalls} tool calls, {Pending}",
            conversation.TurnsUsed,
            o.MaxTurnsPerConversation,
            conversation.Id,
            traces.Count,
            pendingQuote is null ? "no quote" : "quote pending human review");

        await emit(new AgentTurnCompletedEvent(result));
        return result;
    }

    private async Task<AiConversation> LoadOrCreateConversationAsync(
        Guid userId, AgentChatRequest request, CancellationToken ct)
    {
        if (request.ConversationId is { } id)
        {
            var existing = await db.AiConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
            return existing ?? throw new NotFoundException(
                "ConversationNotFound", "Conversation not found.");
        }

        var conversation = new AiConversation { UserId = userId };
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    /// <summary>
    /// A2/A5: WRITE and QUOTE tools require an idempotency key. The loop supplies
    /// one per model tool call and reuses it across retries, so a retried quote
    /// replays instead of creating a second order.
    /// </summary>
    private PreparedInvocation PrepareInvocation(AiToolCall call)
    {
        var policy = toolPolicy.Find(call.Name);
        var needsKey = policy is { RiskClass: ToolRiskClass.Write or ToolRiskClass.Quote };
        var key = needsKey ? Guid.NewGuid().ToString("D") : string.Empty;

        if (!needsKey)
            return new PreparedInvocation(call.Name, call.ArgumentsJson, key);

        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson)
                       as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // Let the tool boundary reject malformed JSON with INVALID_ARGS.
            return new PreparedInvocation(call.Name, call.ArgumentsJson, key);
        }

        if (!args.ContainsKey("idempotencyKey"))
            args["idempotencyKey"] = key;

        return new PreparedInvocation(call.Name, args.ToJsonString(AgentJson.Options), key);
    }

    private async Task<(AgentToolResult Result, int Attempts, double DurationMs)> InvokeWithRetriesAsync(
        IAgentToolSession session,
        PreparedInvocation invocation,
        AgentOptions o,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var maxAttempts = Math.Max(1, o.MaxRetriesPerTool + 1);
        AgentToolResult? result = null;
        var attempts = 0;

        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                result = await session.InvokeAsync(
                    new AgentToolInvocation(
                        invocation.Name, invocation.ArgumentsJson, invocation.IdempotencyKey),
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = AgentToolResult.Error(ToolTransportError, ex.Message, retryable: true);
            }

            if (!result.IsError || !result.Retryable || attempts >= maxAttempts)
                break;

            var delay = TimeSpan.FromMilliseconds(50 * attempts);
            await Task.Delay(delay, ct);
        }

        var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return (result!, attempts, durationMs);
    }

    /// <summary>
    /// A6 Assist lifecycle: a successful create_quote is Executed immediately
    /// (the quote itself is the human-review artifact); a blocked call is
    /// Rejected with the MCP error code. The sweeper flips Executed to Expired
    /// when the unpaid quote dies.
    /// </summary>
    private async Task RecordQuoteActionAsync(
        Guid userId,
        Guid conversationId,
        string idempotencyKey,
        AgentToolResult result,
        AgentQuoteHandoff? handoff,
        CancellationToken ct)
    {
        if (idempotencyKey.Length == 0)
            return;

        var exists = await db.AiActions.AnyAsync(a => a.IdempotencyKey == idempotencyKey, ct);
        if (exists)
            return;

        var action = new AiAction
        {
            UserId = userId,
            ConversationId = conversationId,
            Type = AiActionType.CreateQuote,
            Origin = QuoteOrigin.Agent,
            IdempotencyKey = idempotencyKey,
        };

        if (result.IsError || handoff is null)
        {
            action.Status = AiActionStatus.Rejected;
            action.ErrorCode = result.ErrorCode ?? ToolError;
            action.PayloadJson = "{}";
            action.DecidedAt = DateTime.UtcNow;
        }
        else
        {
            action.Status = AiActionStatus.Executed;
            action.OrderId = handoff.OrderId;
            action.ExpiresAt = handoff.ExpiresAt;
            action.ExecutedAt = DateTime.UtcNow;
            action.DecidedAt = action.ExecutedAt;
            action.PayloadJson = AuditPayload(handoff);
        }

        db.AiActions.Add(action);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Audit-safe: totals and identifiers only, never the shipping address or phone.</summary>
    private static string AuditPayload(AgentQuoteHandoff handoff) =>
        JsonSerializer.Serialize(
            new
            {
                orderId = handoff.OrderId,
                totalAmount = handoff.TotalAmount,
                expiresAt = handoff.ExpiresAt,
            },
            AgentJson.Options);

    private static AgentQuoteHandoff? TryReadHandoff(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object)
                return null;
            if (!order.TryGetProperty("id", out var idElement)
                || !Guid.TryParse(idElement.GetString(), out var orderId))
                return null;
            if (!root.TryGetProperty("hostedPayUrl", out var urlElement))
                return null;
            if (!root.TryGetProperty("expiresAt", out var expiresElement)
                || !expiresElement.TryGetDateTime(out var expiresAt))
                return null;

            var total = order.TryGetProperty("totalAmount", out var totalElement)
                        && totalElement.TryGetDecimal(out var amount)
                ? amount
                : 0m;

            return new AgentQuoteHandoff(
                orderId, urlElement.GetString() ?? string.Empty, expiresAt, total);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToolBudgetReply(int cap) =>
        $"I reached the {cap}-tool-call budget for this turn before I could finish. " +
        "Ask me again with a narrower request (for example, one category and your budget).";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record PreparedInvocation(string Name, string ArgumentsJson, string IdempotencyKey);
}
