using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Shop.Application.Common;
using Shop.Application.Interfaces;
using Shop.Application.Mcp;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.Idempotency;
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

    [Fact]
    public async Task Write_tool_without_an_idempotency_key_is_invalid_args_before_work()
    {
        var store = new FakeIdempotencyStore();
        var invoked = false;

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 1 },
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(new { });
            },
            default);

        Assert.False(invoked);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result)!.Code);
        Assert.Empty(store.Acquired);
    }

    [Fact]
    public async Task Quote_tool_without_an_idempotency_key_is_invalid_args()
    {
        var store = new FakeIdempotencyStore();

        var result = await Create(User(), store: store).RunAsync(
            "create_quote",
            new { fullName = "A", phone = "1", address = "a", city = "c" },
            _ => Task.FromResult<object?>(new { }),
            default);

        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result)!.Code);
        Assert.Empty(store.Acquired);
    }

    [Fact]
    public async Task Read_tool_ignores_an_idempotency_key()
    {
        var store = new FakeIdempotencyStore();

        var result = await Create(User(), store: store).RunAsync(
            "get_cart",
            null,
            _ => Task.FromResult<object?>(new { items = Array.Empty<string>() }),
            default,
            "not-used-by-a-read");

        Assert.False(result.IsError);
        Assert.Empty(store.Acquired);
    }

    [Fact]
    public async Task Acquired_lease_runs_the_action_and_completes_with_the_result()
    {
        var store = new FakeIdempotencyStore();

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 2 },
            _ => Task.FromResult<object?>(new { totalQuantity = 2 }),
            default,
            "key-1");

        Assert.False(result.IsError);
        Assert.Single(store.Acquired);
        Assert.Contains(":add_to_cart:key-1", store.Acquired[0], StringComparison.Ordinal);
        Assert.StartsWith("mcp:", store.Acquired[0], StringComparison.Ordinal);
        Assert.Single(store.Completed);
        Assert.NotNull(store.CompletedResponse);
        Assert.Contains("totalQuantity", store.CompletedResponse!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_returns_the_stored_result_without_running_the_action()
    {
        var store = new FakeIdempotencyStore
        {
            Next = new IdempotencyAcquisition(
                IdempotencyAcquisitionResult.Replay,
                StoredResponse: new StoredIdempotentResponse(
                    200, "application/json", """{"totalQuantity":7}""")),
        };
        var invoked = false;

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 2 },
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(new { totalQuantity = 2 });
            },
            default,
            "key-1");

        Assert.False(invoked);
        Assert.False(result.IsError);
        Assert.Contains("7", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Payload_mismatch_is_not_retryable()
    {
        var store = new FakeIdempotencyStore
        {
            Next = new(IdempotencyAcquisitionResult.PayloadMismatch),
        };

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 2 },
            _ => Task.FromResult<object?>(new { }),
            default,
            "key-1");

        var error = Error(result);
        Assert.Equal(McpErrorCodes.IdempotencyMismatch, error!.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task In_progress_lease_is_retryable_with_the_remaining_seconds()
    {
        var store = new FakeIdempotencyStore
        {
            Next = new(IdempotencyAcquisitionResult.InProgress, RetryAfterSeconds: 9),
        };

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 2 },
            _ => Task.FromResult<object?>(new { }),
            default,
            "key-1");

        var error = Error(result);
        Assert.Equal(McpErrorCodes.IdempotencyInProgress, error!.Code);
        Assert.True(error.Retryable);
        Assert.Contains("9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Store_outage_is_reported_as_retryable_and_the_action_never_runs()
    {
        var store = new FakeIdempotencyStore { Unavailable = true };
        var invoked = false;

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 1 },
            _ =>
            {
                invoked = true;
                return Task.FromResult<object?>(new { });
            },
            default,
            "key-1");

        var error = Error(result);
        Assert.False(invoked);
        Assert.Equal(McpErrorCodes.IdempotencyUnavailable, error!.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task A_failed_write_releases_the_lease_so_a_retry_can_run()
    {
        var store = new FakeIdempotencyStore();

        var result = await Create(User(), store: store).RunAsync(
            "add_to_cart",
            new { productId = Guid.NewGuid(), quantity = 99 },
            _ => Task.FromException<object?>(
                new InsufficientStockException(Guid.NewGuid(), available: 1, requested: 99)),
            default,
            "key-1");

        Assert.Equal(McpErrorCodes.Conflict, Error(result)!.Code);
        Assert.Empty(store.Completed);
        Assert.Single(store.Released);
    }

    [Fact]
    public async Task A_human_held_quote_has_its_own_error_code()
    {
        var result = await Create(User()).RunAsync(
            "create_quote",
            new { fullName = "A", phone = "1", address = "a", city = "c" },
            _ => Task.FromException<object?>(new QuoteHeldByHumanException(Guid.NewGuid())),
            default,
            "key-1");

        var error = Error(result);
        Assert.Equal(McpErrorCodes.QuoteHeldByHuman, error!.Code);
        Assert.False(error.Retryable);
    }

    private static ToolExecutor Create(
        McpToolContext context,
        IMcpBudgetCounter? budget = null,
        McpOptions? options = null,
        IIdempotencyStore? store = null) =>
        new(
            context,
            ToolRiskPolicy.Default,
            budget ?? new AllowAllBudget(),
            store ?? new FakeIdempotencyStore(),
            Options.Create(new IdempotencyOptions { RenewSeconds = 1 }),
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

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public IdempotencyAcquisition Next { get; set; } =
            new(IdempotencyAcquisitionResult.Acquired, LeaseToken: "lease-1");

        public bool Unavailable { get; set; }

        public List<string> Acquired { get; } = [];
        public List<string> Completed { get; } = [];
        public List<string> Released { get; } = [];
        public StoredIdempotentResponse? CompletedResponse { get; private set; }
        public string? RequestHash { get; private set; }

        public Task<IdempotencyAcquisition> TryAcquireAsync(
            string key, string requestHash, CancellationToken ct = default)
        {
            if (Unavailable) throw new IdempotencyUnavailableException("redis down");
            Acquired.Add(key);
            RequestHash = requestHash;
            return Task.FromResult(Next);
        }

        public Task<bool> RenewLeaseAsync(string key, string leaseToken, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> CompleteAsync(
            string key, string leaseToken, StoredIdempotentResponse response, CancellationToken ct = default)
        {
            Completed.Add(key);
            CompletedResponse = response;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseAsync(string key, string leaseToken, CancellationToken ct = default)
        {
            Released.Add(key);
            return Task.FromResult(true);
        }

        public Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<IdempotencyRecord?>(null);
    }
}
