using System.Text.Json;
using Shop.Application.Agent;
using Shop.Application.Ai;
using Shop.Application.Comparisons;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Domain.Enums;

namespace Shop.Infrastructure.Agent;

public sealed class DefaultAgentToolGateway(
    IProductService products,
    ICartService cart,
    IQuoteService quotes,
    IProductComparisonBuilder comparisonBuilder) : IAgentToolGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<AiToolDefinition> ToolCatalog =
    [
        new(
            "search_products",
            "Search catalog products by keyword, category, brand, price, stock, and spec filters.",
            "{\"type\":\"object\",\"properties\":{\"search\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"brand\":{\"type\":\"string\"},\"minPrice\":{\"type\":\"number\"},\"maxPrice\":{\"type\":\"number\"},\"inStockOnly\":{\"type\":\"boolean\"},\"sort\":{\"type\":\"string\"},\"page\":{\"type\":\"integer\",\"minimum\":1},\"pageSize\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50},\"specs\":{\"type\":\"string\"}},\"additionalProperties\":false}"),
        new(
            "get_product",
            "Fetch a single product by id.",
            "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"id\"],\"additionalProperties\":false}"),
        new(
            "compare_products",
            "Compare 2 to 4 products side-by-side.",
            "{\"type\":\"object\",\"properties\":{\"productIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"format\":\"uuid\"},\"minItems\":2,\"maxItems\":4}},\"required\":[\"productIds\"],\"additionalProperties\":false}"),
        new(
            "get_cart",
            "Read the current cart for the authenticated user.",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
        new(
            "add_to_cart",
            "Add one or more units of a product to the authenticated cart.",
            "{\"type\":\"object\",\"properties\":{\"productId\":{\"type\":\"string\",\"format\":\"uuid\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"productId\",\"quantity\"],\"additionalProperties\":false}"),
        new(
            "create_quote",
            "Create a quote for the current cart so the user can review the total and pay.",
            "{\"type\":\"object\",\"properties\":{\"shippingAddress\":{\"type\":\"object\",\"properties\":{\"fullName\":{\"type\":\"string\"},\"phone\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},\"city\":{\"type\":\"string\"}},\"required\":[\"fullName\",\"phone\",\"address\",\"city\"],\"additionalProperties\":false}},\"additionalProperties\":false}"),
    ];

    public Task<IAgentToolSession> OpenSessionAsync(
        Guid userId,
        string role,
        string accessToken,
        string correlationId,
        CancellationToken ct = default)
    {
        return Task.FromResult<IAgentToolSession>(new InMemorySession(this, userId, role, accessToken, correlationId));
    }

    private sealed class InMemorySession(
        DefaultAgentToolGateway owner,
        Guid userId,
        string _role,
        string _accessToken,
        string _correlationId) : IAgentToolSession
    {
        public IReadOnlyList<AiToolDefinition> Tools => ToolCatalog;

        public async Task<AgentToolResult> InvokeAsync(AgentToolInvocation invocation, CancellationToken ct = default)
        {
            try
            {
                return invocation.Name switch
                {
                    "search_products" => await owner.SearchProductsAsync(invocation.ArgumentsJson, ct),
                    "get_product" => await owner.GetProductAsync(userId, invocation.ArgumentsJson, ct),
                    "compare_products" => await owner.CompareProductsAsync(invocation.ArgumentsJson, ct),
                    "get_cart" => await owner.GetCartAsync(userId, ct),
                    "add_to_cart" => await owner.AddToCartAsync(userId, invocation.ArgumentsJson, ct),
                    "create_quote" => await owner.CreateQuoteAsync(userId, invocation.ArgumentsJson, ct),
                    _ => AgentToolResult.Error("TOOL_NOT_FOUND", $"Tool '{invocation.Name}' is not available.", false),
                };
            }
            catch (Exception ex)
            {
                return AgentToolResult.Error("TOOL_ERROR", ex.Message, retryable: false);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private async Task<AgentToolResult> SearchProductsAsync(string argsJson, CancellationToken ct)
    {
        var args = ParseObject(argsJson);
        var page = GetInt(args, "page", 1);
        var pageSize = GetInt(args, "pageSize", 12);
        var query = new ProductQueryParams(
            Search: GetString(args, "search"),
            Category: GetString(args, "category"),
            Brand: GetString(args, "brand"),
            MinPrice: GetDecimal(args, "minPrice"),
            MaxPrice: GetDecimal(args, "maxPrice"),
            InStockOnly: GetBool(args, "inStockOnly"),
            Sort: GetString(args, "sort"),
            Page: page,
            PageSize: pageSize,
            IncludeInactive: false,
            Specs: GetString(args, "specs"));

        var result = await products.QueryAsync(query, ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private async Task<AgentToolResult> GetProductAsync(Guid userId, string argsJson, CancellationToken ct)
    {
        var args = ParseObject(argsJson);
        var id = GetGuid(args, "id");
        var result = await products.GetByIdAsync(id, false, ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private async Task<AgentToolResult> CompareProductsAsync(string argsJson, CancellationToken ct)
    {
        var args = ParseObject(argsJson);
        var ids = GetGuidArray(args, "productIds");
        var result = await comparisonBuilder.BuildAsync(ids, ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private async Task<AgentToolResult> GetCartAsync(Guid userId, CancellationToken ct)
    {
        var result = await cart.GetCartAsync(userId, ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private async Task<AgentToolResult> AddToCartAsync(Guid userId, string argsJson, CancellationToken ct)
    {
        var args = ParseObject(argsJson);
        var result = await cart.AddItemAsync(userId, new AddCartItemRequest(GetGuid(args, "productId"), GetInt(args, "quantity", 1)), ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private async Task<AgentToolResult> CreateQuoteAsync(Guid userId, string argsJson, CancellationToken ct)
    {
        var args = ParseObject(argsJson);
        var shipping = args.TryGetProperty("shippingAddress", out var address) && address.ValueKind == JsonValueKind.Object
            ? address
            : JsonDocument.Parse("{\"fullName\":\"Customer\",\"phone\":\"0000000000\",\"address\":\"1 Demo Street\",\"city\":\"Demo City\"}").RootElement;

        var request = new CreateQuoteRequest(new ShippingAddressDto(
            shipping.GetProperty("fullName").GetString() ?? "Customer",
            shipping.GetProperty("phone").GetString() ?? "0000000000",
            shipping.GetProperty("address").GetString() ?? "1 Demo Street",
            shipping.GetProperty("city").GetString() ?? "Demo City"));

        var result = await quotes.CreateQuoteAsync(userId, request, QuoteOrigin.Agent, ct);
        return AgentToolResult.Ok(JsonSerializer.Serialize(result, Json));
    }

    private static JsonElement ParseObject(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            using var emptyDoc = JsonDocument.Parse("{}");
            return emptyDoc.RootElement.Clone();
        }

        using var parsedDoc = JsonDocument.Parse(argsJson);
        return parsedDoc.RootElement.Clone();
    }

    private static string? GetString(JsonElement obj, string key)
    {
        return obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined
            ? value.GetString()
            : null;
    }

    private static bool? GetBool(JsonElement obj, string key)
    {
        return obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined
            ? value.GetBoolean()
            : null;
    }

    private static decimal? GetDecimal(JsonElement obj, string key)
    {
        return obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined && value.TryGetDecimal(out var number)
            ? number
            : null;
    }

    private static int GetInt(JsonElement obj, string key, int defaultValue = 1)
    {
        return obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined && value.TryGetInt32(out var number)
            ? number
            : defaultValue;
    }

    private static Guid GetGuid(JsonElement obj, string key)
    {
        var value = GetString(obj, key) ?? throw new ArgumentException($"Missing required argument '{key}'.");
        return Guid.Parse(value);
    }

    private static IReadOnlyList<Guid> GetGuidArray(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"Missing required argument '{key}'.");

        var ids = new List<Guid>();
        foreach (var element in value.EnumerateArray())
            ids.Add(Guid.Parse(element.GetString() ?? throw new InvalidOperationException("Product ID must be a GUID.")));
        return ids;
    }
}
