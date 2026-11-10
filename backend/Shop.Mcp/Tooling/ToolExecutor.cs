using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Shop.Application.Common;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.Idempotency;

namespace Shop.Mcp.Tooling;

/// <summary>
/// A1/A2: one guard around every tool call. Authz, payload limits, budgets, the
/// idempotency lease (derived from the risk class), and error mapping are
/// enforced here, not in the tool method; the tool method only contains the
/// Application call. The log never records the token or tool arguments — only
/// the hashed user, tool, status, duration and correlation id.
/// </summary>
public sealed class ToolExecutor(
    McpToolContext context,
    IToolRiskPolicy policy,
    IMcpBudgetCounter budgets,
    IIdempotencyStore idempotency,
    IOptions<IdempotencyOptions> idempotencyOptions,
    IOptions<McpOptions> options,
    ILogger<ToolExecutor> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CallToolResult> RunAsync(
        string toolName,
        object? args,
        Func<CancellationToken, Task<object?>> action,
        CancellationToken ct,
        string? idempotencyKey = null)
    {
        var started = Stopwatch.GetTimestamp();
        var status = "ok";

        try
        {
            var definition = policy.Find(toolName);
            if (definition is null || definition.RiskClass == ToolRiskClass.Never)
            {
                status = "tool_not_found";
                return Fail(McpErrorCodes.ToolNotFound, "Tool is not available.", retryable: false);
            }

            if (!definition.AllowAnonymous && !context.IsAuthenticated)
            {
                status = "unauthorized";
                return Fail(
                    McpErrorCodes.Unauthorized,
                    "A valid user token is required for this tool.",
                    retryable: false);
            }

            var argsBytes = args is null
                ? 0
                : Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(args, Json));
            if (argsBytes > options.Value.MaxArgsBytes)
            {
                status = "invalid_args";
                return Fail(
                    McpErrorCodes.InvalidArgs,
                    $"Tool arguments exceeded the {options.Value.MaxArgsBytes} byte MCP limit.",
                    retryable: false);
            }

            // A2: the requirement is a property of the risk class, not the tool
            // name. A WRITE or QUOTE tool cannot run without a key; a READ tool
            // ignores one if a client sends it.
            string? key = null;
            if (definition.RiskClass is ToolRiskClass.Write or ToolRiskClass.Quote)
            {
                key = NormalizeKey(idempotencyKey);
                if (key is null)
                {
                    status = "invalid_args";
                    return Fail(
                        McpErrorCodes.InvalidArgs,
                        "idempotencyKey is required for this tool (1 to 128 characters).",
                        retryable: false);
                }
            }

            var decision = await budgets.TryConsumeAsync(context.PartitionKey, definition.RiskClass, ct);
            if (!decision.Allowed)
            {
                status = "rate_limited";
                return Fail(
                    McpErrorCodes.RateLimited,
                    $"MCP {definition.RiskClass.ToString().ToLowerInvariant()} budget exceeded. " +
                    $"Retry after {Math.Ceiling(decision.RetryAfter.TotalSeconds)} seconds.",
                    retryable: true);
            }

            IdempotencyLease? lease = null;
            if (key is not null)
            {
                var logicalKey = LogicalKey(key, toolName);
                var acquisition = await idempotency.TryAcquireAsync(logicalKey, RequestHash(args), ct);
                switch (acquisition.Result)
                {
                    case IdempotencyAcquisitionResult.Replay:
                        status = "idempotent_replay";
                        return Replay(acquisition.StoredResponse);

                    case IdempotencyAcquisitionResult.PayloadMismatch:
                        status = "idempotency_mismatch";
                        return Fail(
                            McpErrorCodes.IdempotencyMismatch,
                            "This idempotencyKey was already used with different arguments.",
                            retryable: false);

                    case IdempotencyAcquisitionResult.InProgress:
                        status = "idempotency_in_progress";
                        return Fail(
                            McpErrorCodes.IdempotencyInProgress,
                            "A call with this idempotencyKey is still in progress. " +
                            $"Retry after {acquisition.RetryAfterSeconds ?? 1} seconds.",
                            retryable: true);

                    default:
                        lease = new IdempotencyLease(
                            idempotency,
                            logicalKey,
                            acquisition.LeaseToken!,
                            TimeSpan.FromSeconds(Math.Max(1, idempotencyOptions.Value.RenewSeconds)));
                        lease.Start();
                        break;
                }
            }

            try
            {
                object? result;
                using (CorrelationContext.Begin(context.CorrelationId))
                    result = await action(ct);

                var element = JsonSerializer.SerializeToElement(result, Json);
                var resultBytes = Encoding.UTF8.GetByteCount(element.GetRawText());
                if (resultBytes > options.Value.MaxResultBytes)
                {
                    status = "result_too_large";
                    return Fail(
                        McpErrorCodes.ResultTooLarge,
                        $"Tool result exceeded the {options.Value.MaxResultBytes} byte MCP limit.",
                        retryable: false);
                }

                if (lease is not null)
                    await lease.CompleteAsync(element.GetRawText(), ct);

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = element.GetRawText() }],
                    StructuredContent = element,
                    IsError = false,
                };
            }
            finally
            {
                // An errored attempt (throw, oversized result, cancelled) releases
                // the key so a retry can execute; a completed one is already Done,
                // so the owner-checked release is a no-op.
                if (lease is not null)
                    await lease.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            var (code, message, retryable) = Map(ex);
            status = code.ToLowerInvariant();
            if (code == McpErrorCodes.UpstreamError)
                logger.LogError(ex, "MCP tool {Tool} failed with {Code}", toolName, code);

            return Fail(code, message, retryable);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            logger.LogInformation(
                "MCP tool {Tool} {Status} in {DurationMs:0.0}ms for {UserHash} ({CorrelationId})",
                toolName, status, elapsedMs, UserHash(), context.CorrelationId);
        }
    }

    private static string? NormalizeKey(string? key)
    {
        var trimmed = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        return trimmed is { Length: <= 128 } ? trimmed : null;
    }

    private string LogicalKey(string key, string toolName) =>
        $"mcp:{(context.UserId?.ToString("D") ?? "anon")}:{toolName}:{key}";

    private static string RequestHash(object? args)
    {
        var payload = args is null ? "null" : JsonSerializer.Serialize(args, Json);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private CallToolResult Replay(StoredIdempotentResponse? stored)
    {
        if (stored is null || string.IsNullOrWhiteSpace(stored.Body))
            return Fail(
                McpErrorCodes.UpstreamError,
                "The stored response for this idempotencyKey is unavailable.",
                retryable: true);

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(stored.Body);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = stored.Body }],
                StructuredContent = element,
                IsError = false,
            };
        }
        catch (JsonException)
        {
            return Fail(
                McpErrorCodes.UpstreamError,
                "The stored response for this idempotencyKey is unreadable.",
                retryable: true);
        }
    }

    private CallToolResult Fail(string code, string message, bool retryable)
    {
        var error = new McpToolError(code, message, context.CorrelationId, retryable);
        var element = JsonSerializer.SerializeToElement(error, Json);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element,
            IsError = true,
        };
    }

    private static (string Code, string Message, bool Retryable) Map(Exception ex) => ex switch
    {
        ValidationException ve => (McpErrorCodes.InvalidArgs, ValidationMessage(ve), false),
        DomainValidationException de => (McpErrorCodes.InvalidArgs, de.Message, false),
        InvalidSpecFilterException isf => (McpErrorCodes.InvalidArgs, isf.Message, false),
        ArgumentException ae => (McpErrorCodes.InvalidArgs, ae.Message, false),
        UnauthorizedAccessException => (McpErrorCodes.Unauthorized,
            "A valid user token is required for this tool.", false),
        AgentScopeForbiddenException or UnauthorizedOrderAccessException =>
            (McpErrorCodes.Forbidden, ex.Message, false),
        QuoteHeldByHumanException qh => (McpErrorCodes.QuoteHeldByHuman, qh.Message, false),
        NotFoundException => (McpErrorCodes.NotFound, ex.Message, false),
        IdempotencyUnavailableException iu => (McpErrorCodes.IdempotencyUnavailable, iu.Message, true),
        RateLimitUnavailableException => (McpErrorCodes.UpstreamError,
            "The MCP budget store is unavailable; retry the call later.", true),
        ShopDomainException domain => (domain.Code.Contains("Stock", StringComparison.OrdinalIgnoreCase)
                                       || domain.Code.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
            ? McpErrorCodes.Conflict
            : McpErrorCodes.InvalidArgs, domain.Message, false),
        _ => (McpErrorCodes.UpstreamError, "The tool failed unexpectedly.", true),
    };

    private static string ValidationMessage(ValidationException ex) =>
        string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));

    private string UserHash()
    {
        var raw = context.UserId?.ToString("D") ?? "anonymous";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// A2: owner-checked lease wrapper. Renewal mirrors the REST middleware:
    /// losing the lease stops renewal but never cancels the running action;
    /// disposal releases an incomplete attempt so a retry can execute.
    /// </summary>
    private sealed class IdempotencyLease(
        IIdempotencyStore store,
        string key,
        string leaseToken,
        TimeSpan renewInterval) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task? _renewal;

        public void Start() => _renewal = RenewAsync();

        public Task<bool> CompleteAsync(string body, CancellationToken ct) =>
            store.CompleteAsync(
                key,
                leaseToken,
                new StoredIdempotentResponse(200, "application/json", body),
                ct);

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(renewInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    try
                    {
                        if (!await store.RenewLeaseAsync(key, leaseToken, _stop.Token))
                            return;
                    }
                    catch (IdempotencyUnavailableException)
                    {
                        return; // the lease expires; the caller still gets its result
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            if (_renewal is not null)
            {
                try { await _renewal; }
                catch (Exception) { /* cleanup must not mask the tool result */ }
            }

            try { await store.ReleaseAsync(key, leaseToken); }
            catch (Exception) { /* owner-checked release is best effort; the lease expires */ }
            finally { _stop.Dispose(); }
        }
    }
}
