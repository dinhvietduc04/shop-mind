namespace Shop.Infrastructure.Ai;

/// <summary>
/// A4: one OpenAI-compatible endpoint. Adding a provider is one entry in
/// <see cref="AiProviderPresets"/> — no branch in the adapter.
/// </summary>
public sealed record AiProviderPreset(
    string Name,
    string BaseUrl,
    string DefaultModel,
    bool RequiresApiKey,
    string? SessionHeaderName,
    IReadOnlyDictionary<string, string> Headers);
