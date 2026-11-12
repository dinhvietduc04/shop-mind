using Shop.Application.Requirements;

namespace Shop.Application.Interfaces;

/// <summary>
/// M13 / ADR-0007: requirement text → deterministic structured filters → ranked
/// catalog matches. Explicitly not a recommendation engine: no behavior
/// history, no embeddings, no model score.
/// </summary>
public interface IRequirementSearchService
{
    Task<RequirementSearchResult> SearchAsync(
        RequirementSearchRequest request, CancellationToken ct = default);
}
