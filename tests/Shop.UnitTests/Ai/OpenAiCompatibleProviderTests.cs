using System.Net;
using System.Text;
using System.Text.Json;
using Shop.Application.Ai;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.Ai;

namespace Shop.UnitTests.Ai;

/// <summary>
/// A4: the normalized contract. Every provider difference (preset headers, wire
/// shape, finish reason, error status) is proved through a stub handler, so the
/// suite needs no key and no network.
/// </summary>
public class OpenAiCompatibleProviderTests
{
    private const string TextResponse = """
        {"model":"deepseek-v4.1-flash","choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":7}}
        """;

    [Fact]
    public async Task Request_carries_preset_defaults_and_opencode_headers()
    {
        var handler = new StubHandler(_ => Json(TextResponse));
        var provider = Provider(handler, Options());

        await provider.GenerateAsync(Request());

        var call = Assert.Single(handler.Requests);
        Assert.Equal("https://opencode.ai/inference/go/openai/v1/chat/completions", call.Uri);
        Assert.Equal("Bearer test-key", call.Authorization);
        Assert.Contains("ShopMind", call.UserAgent);
        Assert.Equal("session-1", call.Headers["x-opencode-session"]);

        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("deepseek-v4.1-flash", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(1024, body.RootElement.GetProperty("max_tokens").GetInt32());

        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[3].GetProperty("tool_call_id").GetString());

        var tool = body.RootElement.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("search_products", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public async Task BaseUrl_and_model_overrides_win_over_the_preset()
    {
        var handler = new StubHandler(_ => Json(TextResponse));
        var options = Options();
        options.BaseUrl = "https://example.test/v1/";
        options.Model = "my-model";
        var provider = Provider(handler, options);

        await provider.GenerateAsync(Request());

        var call = Assert.Single(handler.Requests);
        Assert.Equal("https://example.test/v1/chat/completions", call.Uri);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("my-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Text_response_is_normalized()
    {
        var provider = Provider(new StubHandler(_ => Json(TextResponse)), Options());

        var response = await provider.GenerateAsync(Request());

        Assert.Equal("hello", response.Text);
        Assert.Empty(response.ToolCalls);
        Assert.Equal(AiFinishReason.Stop, response.FinishReason);
        Assert.Equal(11, response.Usage.InputTokens);
        Assert.Equal(7, response.Usage.OutputTokens);
        Assert.Equal(18, response.Usage.TotalTokens);
        Assert.Equal("deepseek-v4.1-flash", response.Model);
        Assert.True(response.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Tool_call_response_is_normalized()
    {
        const string body = """
            {"model":"deepseek-v4.1-flash","choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"search_products","arguments":"{\"query\":\"laptop\"}"}},{"id":"call_2","type":"function","function":{"name":"get_cart","arguments":"{}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":5,"completion_tokens":9}}
            """;
        var provider = Provider(new StubHandler(_ => Json(body)), Options());

        var response = await provider.GenerateAsync(Request());

        Assert.Null(response.Text);
        Assert.Equal(AiFinishReason.ToolCalls, response.FinishReason);
        Assert.Collection(
            response.ToolCalls,
            call =>
            {
                Assert.Equal("call_1", call.CallId);
                Assert.Equal("search_products", call.Name);
                Assert.Equal("""{"query":"laptop"}""", call.ArgumentsJson);
            },
            call =>
            {
                Assert.Equal("call_2", call.CallId);
                Assert.Equal("get_cart", call.Name);
                Assert.Equal("{}", call.ArgumentsJson);
            });
    }

    [Theory]
    [InlineData("stop", AiFinishReason.Stop)]
    [InlineData("length", AiFinishReason.Length)]
    [InlineData("tool_calls", AiFinishReason.ToolCalls)]
    [InlineData("content_filter", AiFinishReason.ContentFilter)]
    [InlineData("provider_specific", AiFinishReason.Unknown)]
    [InlineData(null, AiFinishReason.Unknown)]
    public async Task Finish_reasons_map_to_the_normalized_enum(string? reason, AiFinishReason expected)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "m",
            choices = new[] { new { message = new { role = "assistant", content = "hi" }, finish_reason = reason } },
        });
        var provider = Provider(new StubHandler(_ => Json(body)), Options());

        var response = await provider.GenerateAsync(Request());

        Assert.Equal(expected, response.FinishReason);
    }

    [Fact]
    public async Task Retryable_status_retries_then_surfaces_a_retryable_error()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.TooManyRequests));
        var options = Options();
        options.MaxRetries = 2;
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(AiProviderException.ProviderErrorCode, ex.Code);
        Assert.True(ex.Retryable);
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task Transient_status_recovers_within_the_retry_budget()
    {
        var attempts = 0;
        var handler = new StubHandler(_ => ++attempts == 1
            ? Json("{}", HttpStatusCode.TooManyRequests)
            : Json(TextResponse));
        var options = Options();
        options.MaxRetries = 2;
        var provider = Provider(handler, options);

        var response = await provider.GenerateAsync(Request());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("hello", response.Text);
    }

    [Fact]
    public async Task Non_retryable_status_fails_without_a_second_call()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.BadRequest));
        var options = Options();
        options.MaxRetries = 2;
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Single(handler.Requests);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task Missing_key_fails_before_any_http_call()
    {
        var handler = new StubHandler(_ => Json(TextResponse));
        var options = Options();
        options.ApiKey = null;
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Equal(AiProviderException.NotConfiguredCode, ex.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ollama_allows_an_empty_key()
    {
        var handler = new StubHandler(_ => Json(TextResponse));
        var options = Options();
        options.Provider = AiProviderPresets.Ollama;
        options.ApiKey = null;
        var provider = Provider(handler, options);

        await provider.GenerateAsync(Request());

        var call = Assert.Single(handler.Requests);
        Assert.Null(call.Authorization);
        Assert.StartsWith("http://localhost:11434/v1/", call.Uri);
    }

    [Fact]
    public async Task Malformed_body_fails_as_a_non_retryable_provider_error()
    {
        var provider = Provider(new StubHandler(_ => Json("""{"choices":[]}""")), Options());

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Equal(AiProviderException.ProviderErrorCode, ex.Code);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task Unknown_provider_is_reported_as_not_configured()
    {
        var handler = new StubHandler(_ => Json(TextResponse));
        var options = Options();
        options.Provider = "bogus";
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Equal(AiProviderException.NotConfiguredCode, ex.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Network_failure_is_retried_then_reported_as_retryable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection reset"));
        var options = Options();
        options.MaxRetries = 2;
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.Equal(3, handler.Requests.Count);
        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task Timeout_is_surfaced_as_retryable()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(TextResponse);
        });
        var options = Options();
        options.TimeoutSeconds = 1;
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));

        Assert.True(ex.Retryable);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AiProviderPresets.OpenCodeGo)]
    [InlineData(AiProviderPresets.DeepSeek)]
    [InlineData(AiProviderPresets.OpenRouter)]
    [InlineData(AiProviderPresets.OpenAi)]
    [InlineData(AiProviderPresets.Groq)]
    [InlineData(AiProviderPresets.Ollama)]
    public void Every_preset_resolves_with_its_own_defaults(string name)
    {
        var preset = AiProviderPresets.Resolve(new AiOptions { Provider = name });

        Assert.Equal(name, preset.Name);
        Assert.False(string.IsNullOrWhiteSpace(preset.BaseUrl));
        Assert.False(string.IsNullOrWhiteSpace(preset.DefaultModel));
    }

    private static AiOptions Options() => new()
    {
        Provider = AiProviderPresets.OpenCodeGo,
        ApiKey = "test-key",
        SessionId = "session-1",
        MaxRetries = 0,
        RetryBaseDelayMs = 0,
    };

    private static OpenAiCompatibleProvider Provider(HttpMessageHandler handler, AiOptions options) =>
        new(new HttpClient(handler), options);

    private static AiRequest Request() => new(
    [
        new AiSystemMessage("You are a shopping assistant."),
        new AiUserMessage("find a laptop"),
        new AiAssistantMessage(null, [new AiToolCall("call_1", "search_products", """{"query":"laptop"}""")]),
        new AiToolResultMessage("call_1", """{"items":[]}"""),
    ],
    [
        new AiToolDefinition("search_products", "Search the catalog", """{"type":"object","properties":{}}"""),
    ]);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((message, _) => Task.FromResult(respond(message)))
        {
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.ToString(),
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.UserAgent.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
            return await respond(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        string Uri,
        string Body,
        string? Authorization,
        string UserAgent,
        IReadOnlyDictionary<string, string> Headers);
}
