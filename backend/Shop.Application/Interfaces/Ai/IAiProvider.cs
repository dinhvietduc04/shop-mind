using Shop.Application.Ai;

namespace Shop.Application.Interfaces;

/// <summary>
/// A4: the only AI seam the agent loop depends on. Wire formats, presets, retry,
/// and provider quirks stay inside the adapter; swapping a provider is config only.
/// </summary>
public interface IAiProvider
{
    Task<AiResponse> GenerateAsync(AiRequest request, CancellationToken ct = default);
}
