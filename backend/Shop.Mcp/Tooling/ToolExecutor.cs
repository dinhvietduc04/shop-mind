using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Shop.Application.Common;
using Shop.Application.Mcp;
using Shop.Domain.Exceptions;

namespace Shop.Mcp.Tooling;

/// <summary>
/// A1: one guard around every tool call. Authz and budgets are enforced here,
/// not in the tool method; the tool method only contains the Application call.
/// The log never records the token or tool arguments — only the hashed user,
/// tool, status, duration and correlation id.
/// </summary>
public sealed class ToolExecutor(
    McpToolContext context,
    IToolRiskPolicy policy,
    IMcpBudgetCounter budgets,
    IOptions<McpOptions> options,
    ILogger<ToolExecutor> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CallToolResult> RunAsync(
        string toolName,
        object? args,
        Func<CancellationToken, Task<object?>> action,
        CancellationToken ct)
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

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = element.GetRawText() }],
                StructuredContent = element,
                IsError = false,
            };
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
        NotFoundException => (McpErrorCodes.NotFound, ex.Message, false),
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
}
