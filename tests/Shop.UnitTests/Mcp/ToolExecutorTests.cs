using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Shop.Application.Common;
using Shop.Application.Mcp;
using Shop.Domain.Exceptions;
using Shop.Mcp.Tooling;

namespace Shop.UnitTests.Mcp;

/// <summary>
/// A1: every tool call goes through one guard. It is authoritative for auth,
/// budgets, payload limits, error mapping, and the audit log; the tool method
/// only contains the business call.
/// </summary>
public class ToolExecutorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Anonymous_public_read_succeeds_with_structured_content()
    {
        var result = await Create(Anonymous()).RunAsync(
            "search_products",
            new { query = "laptop" },
            _ => Task.FromResult<object?>(new { items = new[] { "a" } }),
            default);

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(JsonValueKind.Object, result.StructuredContent!.Value.ValueKind);
        Assert.Contains("items", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_private_tool_is_unauthorized()
    {
        var invoked = false;
        var result = await Create(Anonymous()).RunAsync(
            "get_cart",
            null,
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(null);
            },
            default);

        Assert.True(result.IsError);
        Assert.False(invoked);
        var error = Error(result);
        Assert.Equal(McpErrorCodes.Unauthorized, error!.Code);
        Assert.False(error.Retryable);
        Assert.Equal("corr", error.CorrelationId);
    }

    [Fact]
    public async Task Budget_denial_is_rate_limited()
    {
        var result = await Create(Anonymous(), new DenyBudget()).RunAsync(
            "search_products",
            null,
            _ => Task.FromResult<object?>(new { }),
            default);

        var error = Error(result);
        Assert.NotNull(error);
        Assert.Equal(McpErrorCodes.RateLimited, error!.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task Validation_failure_is_invalid_args()
    {
        var result = await Create(User()).RunAsync(
            "get_cart",
            null,
            _ => Task.FromException<object?>(
                new ValidationException([new ValidationFailure("pageSize", "pageSize must be <= 50")])),
            default);

        var error = Error(result);
        Assert.NotNull(error);
        Assert.Equal(McpErrorCodes.InvalidArgs, error!.Code);
        Assert.Contains("pageSize", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Product_not_found_maps_to_not_found()
    {
        var result = await Create(User()).RunAsync(
            "get_product",
            null,
            _ => Task.FromException<object?>(new ProductNotFoundException(Guid.NewGuid())),
            default);

        Assert.Equal(McpErrorCodes.NotFound, Error(result)!.Code);
    }

    [Fact]
    public async Task Unexpected_exception_maps_to_upstream_error_without_leaking_details()
    {
        var result = await Create(User()).RunAsync(
            "get_cart",
            null,
            _ => Task.FromException<object?>(new InvalidOperationException("secret db detail")),
            default);

        var error = Error(result);
        Assert.NotNull(error);
        Assert.Equal(McpErrorCodes.UpstreamError, error!.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain("secret db detail", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Oversized_arguments_are_rejected_before_the_tool_runs()
    {
        var invoked = false;
        var result = await Create(Anonymous(), options: new McpOptions { MaxArgsBytes = 10 }).RunAsync(
            "search_products",
            new { query = new string('x', 100) },
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(null);
            },
            default);

        Assert.False(invoked);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result)!.Code);
    }

    [Fact]
    public async Task Oversized_results_are_rejected()
    {
        var result = await Create(Anonymous(), options: new McpOptions { MaxResultBytes = 10 }).RunAsync(
            "search_products",
            null,
            _ => Task.FromResult<object?>(new { big = new string('x', 100) }),
            default);

        Assert.Equal(McpErrorCodes.ResultTooLarge, Error(result)!.Code);
    }

    [Fact]
    public async Task Correlation_context_flows_into_the_tool_call()
    {
        string? observed = null;

        var result = await Create(User()).RunAsync(
            "get_cart",
            null,
            _ =>
            {
                observed = CorrelationContext.Id;
                return Task.FromResult<object?>(new { });
            },
            default);

        Assert.False(result.IsError);
        Assert.Equal("corr", observed);
    }

    [Fact]
    public async Task Never_tool_is_never_executed()
    {
        var invoked = false;
        var result = await Create(User()).RunAsync(
            "confirm_payment",
            null,
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(null);
            },
            default);

        Assert.False(invoked);
        Assert.Equal(McpErrorCodes.ToolNotFound, Error(result)!.Code);
    }

    [Fact]
    public async Task Cancellation_is_rethrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Create(User()).RunAsync(
                "get_cart",
                null,
                _ => Task.FromException<object?>(new OperationCanceledException(cts.Token)),
                cts.Token));
    }

    private static ToolExecutor Create(
        McpToolContext context,
        IMcpBudgetCounter? budget = null,
        McpOptions? options = null) =>
        new(
            context,
            ToolRiskPolicy.Default,
            budget ?? new AllowAllBudget(),
            Options.Create(options ?? new McpOptions()),
            NullLogger<ToolExecutor>.Instance);

    private static McpToolContext Anonymous() =>
        new(null, null, null, "corr", null, "anon:test");

    private static McpToolContext User() =>
        new(Guid.NewGuid(), "Customer", "agent", "corr", null, "user:test");

    private static McpToolError? Error(CallToolResult result) =>
        JsonSerializer.Deserialize<McpToolError>(Text(result), Json);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private sealed class AllowAllBudget : IMcpBudgetCounter
    {
        public Task<McpBudgetDecision> TryConsumeAsync(
            string partition, ToolRiskClass riskClass, CancellationToken ct = default) =>
            Task.FromResult(new McpBudgetDecision(true, 30, 29, TimeSpan.Zero));
    }

    private sealed class DenyBudget : IMcpBudgetCounter
    {
        public Task<McpBudgetDecision> TryConsumeAsync(
            string partition, ToolRiskClass riskClass, CancellationToken ct = default) =>
            Task.FromResult(new McpBudgetDecision(false, 30, 0, TimeSpan.FromSeconds(12)));
    }
}
