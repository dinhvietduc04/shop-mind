using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shop.Application.Ai;
using Shop.Application.Interfaces;
using Shop.Domain.Exceptions;

namespace Shop.Infrastructure.Ai;

/// <summary>
/// A4: one adapter for every endpoint that speaks OpenAI Chat Completions with
/// <c>tools</c>. The wire DTOs stay private; callers see only the normalized
/// <see cref="AiRequest"/>/<see cref="AiResponse"/> contract.
/// </summary>
public sealed class OpenAiCompatibleProvider(HttpClient http, AiOptions options) : IAiProvider
{
    private const string UserAgent = "ShopMind/1.0 (+https://github.com/dinhvietduc04/shop-mind)";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _sessionId =
        string.IsNullOrWhiteSpace(options.SessionId) ? Guid.NewGuid().ToString("N") : options.SessionId;

    private AiProviderPreset? _preset;
    private AiProviderPreset Preset => _preset ??= AiProviderPresets.Resolve(options);

    public async Task<AiResponse> GenerateAsync(AiRequest request, CancellationToken ct = default)
    {
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));
        if (request.MaxTokens <= 0)
            throw new ArgumentException("MaxTokens must be positive.", nameof(request));

        var preset = Preset;
        if (preset.RequiresApiKey && string.IsNullOrWhiteSpace(options.ApiKey))
            throw AiProviderException.NotConfigured(preset.Name);

        var wire = new WireRequest
        {
            Model = request.Model ?? preset.DefaultModel,
            Messages = request.Messages.Select(ToWire).ToList(),
            Tools = request.Tools is { Count: > 0 } tools ? tools.Select(ToWire).ToList() : null,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
        };

        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 20 : options.TimeoutSeconds));

        using var response = await SendAsync(preset, wire, timeout.Token, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw AiProviderException.ProviderError(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        return Parse(body, preset.DefaultModel, Stopwatch.GetElapsedTime(started));
    }

    private async Task<HttpResponseMessage> SendAsync(
        AiProviderPreset preset,
        WireRequest wire,
        CancellationToken timeoutToken,
        CancellationToken ct)
    {
        var endpoint = $"{preset.BaseUrl}/chat/completions";
        var maxRetries = Math.Max(0, options.MaxRetries);

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var message = BuildRequest(preset, endpoint, wire);
                try
                {
                    var response = await http.SendAsync(message, timeoutToken).ConfigureAwait(false);
                    if (!AiProviderException.IsRetryableStatus(response.StatusCode) || attempt >= maxRetries)
                        return response;
                    response.Dispose();
                }
                catch (HttpRequestException ex) when (attempt >= maxRetries)
                {
                    throw AiProviderException.Unreachable(ex);
                }
                catch (HttpRequestException)
                {
                    // A transient network fault retries on the next attempt.
                }

                var delay = RetryDelay(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, timeoutToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw AiProviderException.TimedOut();
        }
    }

    private HttpRequestMessage BuildRequest(AiProviderPreset preset, string endpoint, WireRequest wire)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(wire, options: Json),
        };
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        foreach (var (name, value) in preset.Headers)
            message.Headers.TryAddWithoutValidation(name, value);
        if (!string.IsNullOrWhiteSpace(preset.SessionHeaderName))
            message.Headers.TryAddWithoutValidation(preset.SessionHeaderName, _sessionId);
        return message;
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var baseMs = Math.Max(0, options.RetryBaseDelayMs);
        if (baseMs == 0)
            return TimeSpan.Zero;
        var backoff = baseMs * (1 << Math.Min(attempt, 5));
        return TimeSpan.FromMilliseconds(backoff + Random.Shared.Next(0, baseMs + 1));
    }

    private static AiResponse Parse(string body, string fallbackModel, TimeSpan duration)
    {
        WireResponse? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireResponse>(body, Json);
        }
        catch (JsonException)
        {
            throw AiProviderException.Malformed("invalid JSON");
        }

        var choice = wire?.Choices?.FirstOrDefault()
            ?? throw AiProviderException.Malformed("no choices");
        var text = choice.Message?.Content;
        var calls = choice.Message?.ToolCalls?.Select(ToContract).ToList() ?? [];
        if (string.IsNullOrWhiteSpace(text) && calls.Count == 0)
            throw AiProviderException.Malformed("empty message");

        return new AiResponse(
            string.IsNullOrWhiteSpace(text) ? null : text,
            calls,
            new AiUsage(wire!.Usage?.PromptTokens ?? 0, wire.Usage?.CompletionTokens ?? 0),
            string.IsNullOrWhiteSpace(wire.Model) ? fallbackModel : wire.Model,
            duration,
            MapFinish(choice.FinishReason));
    }

    private static AiToolCall ToContract(WireInboundToolCall call)
    {
        var function = call.Function;
        if (string.IsNullOrWhiteSpace(call.Id) || function is null || string.IsNullOrWhiteSpace(function.Name))
            throw AiProviderException.Malformed("tool call missing id or name");
        return new AiToolCall(
            call.Id,
            function.Name,
            string.IsNullOrWhiteSpace(function.Arguments) ? "{}" : function.Arguments);
    }

    private static WireMessage ToWire(AiMessage message) => message switch
    {
        AiSystemMessage m => new WireMessage { Role = "system", Content = m.Content },
        AiUserMessage m => new WireMessage { Role = "user", Content = m.Content },
        AiAssistantMessage m => new WireMessage
        {
            Role = "assistant",
            Content = m.Text,
            ToolCalls = m.ToolCalls.Count == 0
                ? null
                : m.ToolCalls.Select(c => new WireOutboundToolCall
                {
                    Id = c.CallId,
                    Function = new WireOutboundFunction { Name = c.Name, Arguments = c.ArgumentsJson },
                }).ToList(),
        },
        AiToolResultMessage m => new WireMessage
        {
            Role = "tool",
            ToolCallId = m.CallId,
            Content = m.Content,
        },
        _ => throw new ArgumentException($"Unknown AI message type '{message.GetType().Name}'.", nameof(message)),
    };

    private static WireTool ToWire(AiToolDefinition tool)
    {
        JsonElement schema;
        try
        {
            schema = JsonSerializer.Deserialize<JsonElement>(tool.ParametersJsonSchema);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"Tool '{tool.Name}' has an invalid JSON schema.", nameof(tool));
        }

        return new WireTool
        {
            Function = new WireToolFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = schema,
            },
        };
    }

    private static AiFinishReason MapFinish(string? reason) => reason switch
    {
        "stop" => AiFinishReason.Stop,
        "length" => AiFinishReason.Length,
        "tool_calls" => AiFinishReason.ToolCalls,
        "content_filter" => AiFinishReason.ContentFilter,
        _ => AiFinishReason.Unknown,
    };

    private sealed record WireRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("messages")] public required List<WireMessage> Messages { get; init; }
        [JsonPropertyName("tools")] public List<WireTool>? Tools { get; init; }
        [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
        [JsonPropertyName("temperature")] public double? Temperature { get; init; }
    }

    private sealed record WireMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("tool_calls")] public List<WireOutboundToolCall>? ToolCalls { get; init; }
        [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; init; }
    }

    private sealed record WireTool
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "function";
        [JsonPropertyName("function")] public required WireToolFunction Function { get; init; }
    }

    private sealed record WireToolFunction
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("description")] public required string Description { get; init; }
        [JsonPropertyName("parameters")] public required JsonElement Parameters { get; init; }
    }

    private sealed record WireOutboundToolCall
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("type")] public string Type { get; init; } = "function";
        [JsonPropertyName("function")] public required WireOutboundFunction Function { get; init; }
    }

    private sealed record WireOutboundFunction
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("arguments")] public required string Arguments { get; init; }
    }

    private sealed record WireResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("choices")] public List<WireChoice>? Choices { get; init; }
        [JsonPropertyName("usage")] public WireUsage? Usage { get; init; }
    }

    private sealed record WireChoice
    {
        [JsonPropertyName("message")] public WireResponseMessage? Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    private sealed record WireResponseMessage
    {
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("tool_calls")] public List<WireInboundToolCall>? ToolCalls { get; init; }
    }

    private sealed record WireInboundToolCall
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("function")] public WireInboundFunction? Function { get; init; }
    }

    private sealed record WireInboundFunction
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("arguments")] public string? Arguments { get; init; }
    }

    private sealed record WireUsage
    {
        [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
    }
}
