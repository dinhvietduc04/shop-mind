using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;
using Shop.Mcp.Tooling;

namespace Shop.IntegrationTests;

/// <summary>
/// A1 acceptance: the real MCP HTTP transport lists only the policy-approved
/// tools, anonymous catalog reads work, and user-scoped tools are ownership
/// checked through the delegated JWT.
/// </summary>
public class McpHttpTests(McpWebFactory factory) : IClassFixture<McpWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Manifest_lists_only_policy_approved_tools()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "add_to_cart", "compare_products", "create_quote", "find_products_for_requirement",
                "get_cart", "get_order", "get_order_status", "get_orders", "get_product",
                "get_product_facets", "prepare_checkout", "remove_from_cart", "search_products",
                "update_cart",
            ],
            names);
    }

    [Fact]
    public async Task Search_products_works_anonymously_and_matches_seed_data()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["category"] = "laptops", ["pageSize"] = 50 });

        Assert.False(result.IsError);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_cart_without_a_token_is_unauthorized()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.Unauthorized, Error(result).Code);
    }

    [Fact]
    public async Task Get_cart_returns_only_the_token_owners_cart()
    {
        var (aliceId, aliceToken) = await CreateUserWithTokenAsync("alice");
        var (bobId, bobToken) = await CreateUserWithTokenAsync("bob");
        _ = bobId;
        var productId = await AddProductToCartAsync(aliceId);

        await using var alice = await McpTestClient.ConnectAsync(factory, aliceToken);
        await using var bob = await McpTestClient.ConnectAsync(factory, bobToken);

        var aliceResult = await alice.CallToolAsync("get_cart", new Dictionary<string, object?>());
        var bobResult = await bob.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.False(aliceResult.IsError);
        Assert.False(bobResult.IsError);
        Assert.Contains(productId.ToString(), Text(aliceResult), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(productId.ToString(), Text(bobResult), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_tool_argument_is_rejected()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["bogus"] = 1 });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Oversized_arguments_are_rejected()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["query"] = new string('x', 5_000) });

        Assert.True(result.IsError);
        var error = Error(result);
        Assert.Equal(McpErrorCodes.InvalidArgs, error.Code);
        Assert.Contains("4096", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_product_returns_the_seeded_detail()
    {
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product",
            new Dictionary<string, object?> { ["productId"] = productId.ToString() });

        Assert.False(result.IsError);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_facets_work_anonymously()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product_facets",
            new Dictionary<string, object?> { ["category"] = "headphones" });

        Assert.False(result.IsError);
        Assert.Contains("battery_h", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_products_for_requirement_applies_deterministic_filters_anonymously()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "find_products_for_requirement",
            new Dictionary<string, object?>
            {
                ["requirementText"] = "laptop for backend dev under $1600",
            });

        Assert.False(result.IsError);
        var payload = Payload(result);

        Assert.Equal("laptops", payload.GetProperty("category").GetString());
        Assert.Equal(1600m, payload.GetProperty("budget").GetDecimal());
        Assert.Contains(
            payload.GetProperty("appliedFilters").EnumerateArray().Select(f => f.GetString()),
            filter => filter!.Contains("RAM (GB) >= 16", StringComparison.Ordinal));

        var names = payload.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("ThinkPad X1 Carbon Gen 11", names);
        Assert.DoesNotContain("MacBook Air M3", names);
    }

    [Fact]
    public async Task Find_products_for_requirement_fails_closed_on_an_unknown_category_hint()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "find_products_for_requirement",
            new Dictionary<string, object?>
            {
                ["requirementText"] = "something", ["categoryHint"] = "toasters",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Token_is_never_written_to_logs()
    {
        var (_, token) = await CreateUserWithTokenAsync("logcheck");
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        await client.CallToolAsync("get_cart", new Dictionary<string, object?>());

        Assert.DoesNotContain(
            TestLogSink.Instance.Messages,
            message => message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_page_size_is_invalid_args()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["pageSize"] = 51 });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Get_product_with_an_unknown_id_is_not_found()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "get_product",
            new Dictionary<string, object?> { ["productId"] = Guid.NewGuid().ToString() });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.NotFound, Error(result).Code);
    }

    [Fact]
    public async Task Add_to_cart_uses_the_REST_quantity_validator()
    {
        var (userId, token) = await CreateUserWithTokenAsync("bad-qty");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 0, ["idempotencyKey"] = "zero",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
        Assert.Empty(await CartLinesAsync(userId));
    }

    [Fact]
    public async Task Create_quote_uses_the_REST_address_validator()
    {
        var (userId, token) = await CreateUserWithTokenAsync("bad-address");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "addr-add",
            });
        var result = await client.CallToolAsync(
            "create_quote",
            new Dictionary<string, object?>
            {
                ["fullName"] = "", ["phone"] = "1", ["address"] = "a", ["city"] = "c",
                ["idempotencyKey"] = "addr-quote",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
        Assert.Empty(await OrdersFromDbAsync(userId));
    }

    [Fact]
    public async Task Compare_products_returns_a_deterministic_table_anonymously()
    {
        var thinkpad = await ProductIdBySkuAsync("LEN-X1C-G11");
        var macbook = await ProductIdBySkuAsync("APL-MBA-M3");
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "compare_products",
            new Dictionary<string, object?> { ["productIds"] = new[] { thinkpad, macbook } });

        Assert.False(result.IsError);
        var payload = Payload(result);
        Assert.Equal(2, payload.GetProperty("products").GetArrayLength());
        Assert.Equal(thinkpad, payload.GetProperty("products")[0].GetProperty("id").GetGuid());

        var rows = payload.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToArray();
        Assert.Contains("ram_gb", rows);
        Assert.Contains("thunderbolt", rows);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("reserved", Text(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compare_products_rejects_a_single_product()
    {
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "compare_products",
            new Dictionary<string, object?> { ["productIds"] = new[] { productId } });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Search_products_filters_by_specs()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?>
            {
                ["category"] = "laptops",
                ["specs"] = new[]
                {
                    new Dictionary<string, object?> { ["key"] = "ram_gb", ["op"] = "gte", ["value"] = 16 },
                },
            });

        Assert.False(result.IsError);
        Assert.Contains("ThinkPad X1 Carbon Gen 11", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("MacBook Air M3", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_products_rejects_an_unregistered_spec_key()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?>
            {
                ["specs"] = new[]
                {
                    new Dictionary<string, object?> { ["key"] = "made_up", ["op"] = "eq", ["value"] = "x" },
                },
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    [Fact]
    public async Task Add_to_cart_without_an_idempotency_key_is_invalid_args()
    {
        var (userId, token) = await CreateUserWithTokenAsync("nokey");
        var productId = await FirstProductIdAsync();

        await using var client = await McpTestClient.ConnectAsync(factory, token);
        var result = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?> { ["productId"] = productId, ["quantity"] = 1 });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
        Assert.Empty(await CartLinesAsync(userId));
    }

    [Fact]
    public async Task Add_to_cart_replays_the_same_key_and_adds_again_with_a_new_one()
    {
        var (userId, token) = await CreateUserWithTokenAsync("idem");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var first = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "add-1",
            });
        var replay = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "add-1",
            });
        var second = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 2, ["idempotencyKey"] = "add-2",
            });

        Assert.False(first.IsError);
        Assert.False(replay.IsError);
        Assert.False(second.IsError);
        Assert.Equal(1, Payload(first).GetProperty("totalQuantity").GetInt32());
        Assert.Equal(1, Payload(replay).GetProperty("totalQuantity").GetInt32());
        Assert.Equal(3, Payload(second).GetProperty("totalQuantity").GetInt32());

        var lines = await CartLinesAsync(userId);
        Assert.Single(lines);
        Assert.Equal(3, lines[0].Quantity);
    }

    [Fact]
    public async Task Reusing_a_key_with_different_arguments_is_a_mismatch()
    {
        var (_, token) = await CreateUserWithTokenAsync("mismatch");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "same-key",
            });
        var result = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 2, ["idempotencyKey"] = "same-key",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.IdempotencyMismatch, Error(result).Code);
    }

    [Fact]
    public async Task Cart_writes_are_owner_scoped_across_two_tokens()
    {
        var (aliceId, aliceToken) = await CreateUserWithTokenAsync("cart-alice");
        var (_, bobToken) = await CreateUserWithTokenAsync("cart-bob");
        var productId = await FirstProductIdAsync();

        await using var alice = await McpTestClient.ConnectAsync(factory, aliceToken);
        await using var bob = await McpTestClient.ConnectAsync(factory, bobToken);

        await alice.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "alice-add",
            });
        var itemId = (await CartLinesAsync(aliceId)).Single().Id;

        var bobCart = await bob.CallToolAsync("get_cart", new Dictionary<string, object?>());
        var bobUpdate = await bob.CallToolAsync(
            "update_cart",
            new Dictionary<string, object?>
            {
                ["cartItemId"] = itemId, ["quantity"] = 5, ["idempotencyKey"] = "bob-update",
            });
        var bobRemove = await bob.CallToolAsync(
            "remove_from_cart",
            new Dictionary<string, object?>
            {
                ["cartItemId"] = itemId, ["idempotencyKey"] = "bob-remove",
            });

        Assert.DoesNotContain(productId.ToString(), Text(bobCart), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpErrorCodes.NotFound, Error(bobUpdate).Code);
        Assert.Equal(McpErrorCodes.NotFound, Error(bobRemove).Code);
        Assert.Equal(1, (await CartLinesAsync(aliceId)).Single().Quantity);
    }

    [Fact]
    public async Task Update_and_remove_cart_are_idempotent_and_return_the_cart()
    {
        var (userId, token) = await CreateUserWithTokenAsync("edit");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = "e-add",
            });
        var itemId = (await CartLinesAsync(userId)).Single().Id;

        var updated = await client.CallToolAsync(
            "update_cart",
            new Dictionary<string, object?>
            {
                ["cartItemId"] = itemId, ["quantity"] = 4, ["idempotencyKey"] = "e-update",
            });
        var removed = await client.CallToolAsync(
            "remove_from_cart",
            new Dictionary<string, object?>
            {
                ["cartItemId"] = itemId, ["idempotencyKey"] = "e-remove",
            });

        Assert.False(updated.IsError);
        Assert.Equal(4, Payload(updated).GetProperty("totalQuantity").GetInt32());
        Assert.False(removed.IsError);
        Assert.Equal(0, Payload(removed).GetProperty("totalQuantity").GetInt32());
        Assert.Empty(await CartLinesAsync(userId));
    }

    [Fact]
    public async Task Add_to_cart_over_stock_is_a_conflict_and_writes_nothing()
    {
        var (userId, token) = await CreateUserWithTokenAsync("oversell");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 99_999, ["idempotencyKey"] = "too-many",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.Conflict, Error(result).Code);
        Assert.Empty(await CartLinesAsync(userId));
    }

    [Fact]
    public async Task Order_reads_are_owner_scoped_and_status_is_a_projection()
    {
        var (aliceId, aliceToken) = await CreateUserWithTokenAsync("orders-alice");
        var (_, bobToken) = await CreateUserWithTokenAsync("orders-bob");
        var productId = await FirstProductIdAsync();

        await using var alice = await McpTestClient.ConnectAsync(factory, aliceToken);
        await using var bob = await McpTestClient.ConnectAsync(factory, bobToken);

        var orderId = await CreateAgentQuoteAsync(alice, productId, "orders-quote");

        var aliceList = await alice.CallToolAsync("get_orders", new Dictionary<string, object?>());
        var bobList = await bob.CallToolAsync("get_orders", new Dictionary<string, object?>());
        var bobGet = await bob.CallToolAsync(
            "get_order", new Dictionary<string, object?> { ["orderId"] = orderId });
        var bobStatus = await bob.CallToolAsync(
            "get_order_status", new Dictionary<string, object?> { ["orderId"] = orderId });
        var aliceStatus = await alice.CallToolAsync(
            "get_order_status", new Dictionary<string, object?> { ["orderId"] = orderId });

        Assert.Contains(orderId.ToString(), Text(aliceList), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orderId.ToString(), Text(bobList), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpErrorCodes.Forbidden, Error(bobGet).Code);
        Assert.Equal(McpErrorCodes.Forbidden, Error(bobStatus).Code);
        Assert.False(aliceStatus.IsError);
        Assert.Equal("Pending", Payload(aliceStatus).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_order_with_an_unknown_id_is_not_found()
    {
        var (_, token) = await CreateUserWithTokenAsync("missing-order");
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync(
            "get_order", new Dictionary<string, object?> { ["orderId"] = Guid.NewGuid() });

        Assert.Equal(McpErrorCodes.NotFound, Error(result).Code);
    }

    [Fact]
    public async Task Create_quote_records_agent_origin_and_replays_the_same_key()
    {
        var (userId, token) = await CreateUserWithTokenAsync("quote-agent");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var firstId = await CreateAgentQuoteAsync(client, productId, "quote-key");
        var replayId = await CreateAgentQuoteAsync(client, productId, "quote-key");

        Assert.Equal(firstId, replayId);
        var orders = await OrdersFromDbAsync(userId);
        Assert.Single(orders);
        Assert.Equal(QuoteOrigin.Agent, orders[0].Origin);
        Assert.Equal(OrderStatus.Pending, orders[0].Status);
    }

    [Fact]
    public async Task A_second_agent_quote_replaces_the_first_agent_quote()
    {
        var (userId, token) = await CreateUserWithTokenAsync("quote-replace");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var firstId = await CreateAgentQuoteAsync(client, productId, "agent-quote-1");
        var secondId = await CreateAgentQuoteAsync(client, productId, "agent-quote-2");

        Assert.NotEqual(firstId, secondId);
        var orders = await OrdersFromDbAsync(userId);
        Assert.Equal(OrderStatus.Cancelled, orders.Single(o => o.Id == firstId).Status);
        Assert.Equal(OrderStatus.Pending, orders.Single(o => o.Id == secondId).Status);
    }

    [Fact]
    public async Task An_agent_quote_is_blocked_by_a_human_open_quote()
    {
        var (userId, token) = await CreateUserWithTokenAsync("quote-human");
        var productId = await FirstProductIdAsync();
        var humanOrderId = await CreateHumanQuoteAsync(userId, productId);
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync(
            "create_quote",
            new Dictionary<string, object?>
            {
                ["fullName"] = "A", ["phone"] = "1", ["address"] = "a", ["city"] = "c",
                ["idempotencyKey"] = "blocked-quote",
            });

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.QuoteHeldByHuman, Error(result).Code);

        var orders = await OrdersFromDbAsync(userId);
        Assert.Single(orders);
        Assert.Equal(OrderStatus.Pending, orders[0].Status);
        Assert.Equal(QuoteOrigin.Human, orders[0].Origin);
        Assert.Equal(1, orders[0].Items.Single().ReservedQuantity);
    }

    [Fact]
    public async Task A_human_quote_may_replace_an_agent_quote()
    {
        var (userId, token) = await CreateUserWithTokenAsync("quote-human-wins");
        var productId = await FirstProductIdAsync();
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var agentOrderId = await CreateAgentQuoteAsync(client, productId, "agent-quote");

        using var scope = factory.Services.CreateScope();
        var quotes = scope.ServiceProvider.GetRequiredService<IQuoteService>();
        var human = await quotes.CreateQuoteAsync(
            userId,
            new CreateQuoteRequest(new ShippingAddressDto("H", "1", "1 H St", "Town")),
            QuoteOrigin.Human);

        var orders = await OrdersFromDbAsync(userId);
        Assert.Equal(OrderStatus.Cancelled, orders.Single(o => o.Id == agentOrderId).Status);
        Assert.Equal(QuoteOrigin.Human, orders.Single(o => o.Id == human.Order.Id).Origin);
    }

    [Fact]
    public async Task Scripted_shopping_scenario_matches_the_REST_cart_totals()
    {
        var (_, token) = await CreateUserWithTokenAsync("scenario");
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var search = await client.CallToolAsync(
            "search_products",
            new Dictionary<string, object?> { ["category"] = "laptops", ["pageSize"] = 10 });
        Assert.False(search.IsError);
        var productIds = Payload(search).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Take(2)
            .ToArray();
        Assert.Equal(2, productIds.Length);

        foreach (var productId in productIds)
        {
            var detail = await client.CallToolAsync(
                "get_product", new Dictionary<string, object?> { ["productId"] = productId });
            Assert.False(detail.IsError);
        }

        var compare = await client.CallToolAsync(
            "compare_products", new Dictionary<string, object?> { ["productIds"] = productIds });
        Assert.False(compare.IsError);

        var added = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productIds[0], ["quantity"] = 2, ["idempotencyKey"] = "scenario-add",
            });
        Assert.False(added.IsError);

        var cart = await client.CallToolAsync("get_cart", new Dictionary<string, object?>());
        var quoteResult = await client.CallToolAsync(
            "create_quote",
            new Dictionary<string, object?>
            {
                ["fullName"] = "S", ["phone"] = "1", ["address"] = "a", ["city"] = "c",
                ["idempotencyKey"] = "scenario-quote",
            });
        Assert.False(quoteResult.IsError);

        var cartPayload = Payload(cart);
        var quote = Payload(quoteResult).GetProperty("order");
        var subtotal = cartPayload.GetProperty("subtotal").GetDecimal();
        var expectedShipping = subtotal >= 500m ? 0m : 15m;

        Assert.Equal(cartPayload.GetProperty("items").GetArrayLength(), quote.GetProperty("items").GetArrayLength());
        Assert.Equal(subtotal, quote.GetProperty("subtotal").GetDecimal());
        Assert.Equal(subtotal + expectedShipping, quote.GetProperty("totalAmount").GetDecimal());
        Assert.StartsWith("/pay/", Payload(quoteResult).GetProperty("hostedPayUrl").GetString());
    }

    [Fact]
    public async Task Prepare_checkout_previews_the_exact_bill_without_creating_anything()
    {
        var (userId, token) = await CreateUserWithTokenAsync("preview");
        var productId = await ProductIdBySkuAsync("LEN-X1C-G11");
        var reservedBefore = await ReservedQuantityAsync(productId);
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var added = await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 2, ["idempotencyKey"] = "preview-add",
            });
        Assert.False(added.IsError);

        var result = await client.CallToolAsync("prepare_checkout", new Dictionary<string, object?>());

        Assert.False(result.IsError);
        var preview = Payload(result);
        Assert.Equal(2998m, preview.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, preview.GetProperty("shippingFee").GetDecimal());
        Assert.Equal(2998m, preview.GetProperty("totalAmount").GetDecimal());
        Assert.True(preview.GetProperty("canQuote").GetBoolean());
        Assert.Equal(1, preview.GetProperty("items").GetArrayLength());

        Assert.Empty(await OrdersFromDbAsync(userId));
        Assert.Equal(reservedBefore, await ReservedQuantityAsync(productId));
        Assert.Single(await CartLinesAsync(userId));
    }

    [Fact]
    public async Task Prepare_checkout_without_a_token_is_unauthorized()
    {
        await using var client = await McpTestClient.ConnectAsync(factory);

        var result = await client.CallToolAsync("prepare_checkout", new Dictionary<string, object?>());

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.Unauthorized, Error(result).Code);
    }

    [Fact]
    public async Task Prepare_checkout_with_an_empty_cart_is_invalid_args()
    {
        var (_, token) = await CreateUserWithTokenAsync("preview-empty");
        await using var client = await McpTestClient.ConnectAsync(factory, token);

        var result = await client.CallToolAsync("prepare_checkout", new Dictionary<string, object?>());

        Assert.True(result.IsError);
        Assert.Equal(McpErrorCodes.InvalidArgs, Error(result).Code);
    }

    private async Task<Guid> CreateAgentQuoteAsync(McpClient client, Guid productId, string key)
    {
        await client.CallToolAsync(
            "add_to_cart",
            new Dictionary<string, object?>
            {
                ["productId"] = productId, ["quantity"] = 1, ["idempotencyKey"] = key + "-add",
            });

        var result = await client.CallToolAsync(
            "create_quote",
            new Dictionary<string, object?>
            {
                ["fullName"] = "A", ["phone"] = "1", ["address"] = "a", ["city"] = "c",
                ["idempotencyKey"] = key,
            });
        Assert.False(result.IsError);
        return Payload(result).GetProperty("order").GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateHumanQuoteAsync(Guid userId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
        var quotes = scope.ServiceProvider.GetRequiredService<IQuoteService>();

        await carts.AddItemAsync(userId, new AddCartItemRequest(productId, 1));
        var quote = await quotes.CreateQuoteAsync(
            userId,
            new CreateQuoteRequest(new ShippingAddressDto("H", "1", "1 H St", "Town")),
            QuoteOrigin.Human);
        return quote.Order.Id;
    }

    private async Task<Guid> ProductIdBySkuAsync(string sku)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.Where(p => p.SKU == sku).Select(p => p.Id).SingleAsync();
    }

    private async Task<List<CartItem>> CartLinesAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CartItems.AsNoTracking()
            .Where(item => item.Cart!.UserId == userId)
            .ToListAsync();
    }

    private async Task<List<Order>> OrdersFromDbAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .ToListAsync();
    }

    private async Task<int> ReservedQuantityAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Inventories.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .Select(i => i.ReservedQuantity)
            .SingleAsync();
    }

    private static JsonElement Payload(CallToolResult result) =>
        JsonDocument.Parse(Text(result)).RootElement.Clone();

    private async Task<(Guid UserId, string Token)> CreateUserWithTokenAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Email = $"{name}-{Guid.NewGuid():N}@mcp.test",
            PasswordHash = "not-used",
            FirstName = name,
            LastName = "Test",
            Role = UserRole.Customer,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (user.Id, TestAgentTokens.AgentToken(user.Id));
    }

    private async Task<Guid> FirstProductIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products
            .Where(p => p.SKU == "LEN-X1C-G11")
            .Select(p => p.Id)
            .SingleAsync();
    }

    private async Task<Guid> AddProductToCartAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = db.Products.OrderBy(p => p.Name).First();
        var cart = new Cart { UserId = userId };
        cart.Items.Add(new CartItem
        {
            ProductId = product.Id,
            Quantity = 1,
            UnitPrice = product.Price,
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        return product.Id;
    }

    private static McpToolError Error(CallToolResult result) =>
        JsonSerializer.Deserialize<McpToolError>(Text(result), Json)!;

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
