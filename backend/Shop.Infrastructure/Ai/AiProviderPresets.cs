using Shop.Domain.Exceptions;

namespace Shop.Infrastructure.Ai;

/// <summary>
/// A4: the preset table. Every entry speaks OpenAI Chat Completions; a preset
/// supplies a base URL, a default model, and the headers that endpoint expects.
/// </summary>
public static class AiProviderPresets
{
    public const string OpenCodeGo = "opencode-go";
    public const string DeepSeek = "deepseek";
    public const string OpenRouter = "openrouter";
    public const string OpenAi = "openai";
    public const string Groq = "groq";
    public const string Ollama = "ollama";
    public const string Fake = "fake";

    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    private static readonly IReadOnlyDictionary<string, string> OpenRouterHeaders =
        new Dictionary<string, string>
        {
            ["HTTP-Referer"] = "https://github.com/dinhvietduc04/shop-mind",
            ["X-Title"] = "ShopMind",
        };

    public static readonly IReadOnlyList<AiProviderPreset> All =
    [
        new(OpenCodeGo, "https://opencode.ai/inference/go/openai/v1", "deepseek-v4.1-flash",
            RequiresApiKey: true, SessionHeaderName: "x-opencode-session", NoHeaders),
        new(DeepSeek, "https://api.deepseek.com/v1", "deepseek-chat",
            RequiresApiKey: true, SessionHeaderName: null, NoHeaders),
        new(OpenRouter, "https://openrouter.ai/api/v1", "deepseek/deepseek-chat",
            RequiresApiKey: true, SessionHeaderName: null, OpenRouterHeaders),
        new(OpenAi, "https://api.openai.com/v1", "gpt-4o-mini",
            RequiresApiKey: true, SessionHeaderName: null, NoHeaders),
        new(Groq, "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile",
            RequiresApiKey: true, SessionHeaderName: null, NoHeaders),
        new(Ollama, "http://localhost:11434/v1", "llama3.1",
            RequiresApiKey: false, SessionHeaderName: null, NoHeaders),
    ];

    private static readonly IReadOnlyDictionary<string, AiProviderPreset> ByName =
        All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the configured preset and fold in the <see cref="AiOptions"/>
    /// overrides. Unknown provider and bad override are configuration failures,
    /// surfaced only when an AI call happens.
    /// </summary>
    public static AiProviderPreset Resolve(AiOptions options)
    {
        if (!ByName.TryGetValue(options.Provider?.Trim() ?? "", out var preset))
            throw AiProviderException.NotConfigured(options.Provider ?? "");

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? preset.BaseUrl : options.BaseUrl.Trim();
        var model = string.IsNullOrWhiteSpace(options.Model) ? preset.DefaultModel : options.Model.Trim();
        return preset with { BaseUrl = baseUrl.TrimEnd('/'), DefaultModel = model };
    }
}
