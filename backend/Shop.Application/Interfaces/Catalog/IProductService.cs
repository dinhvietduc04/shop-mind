using Shop.Application.Common;
using Shop.Application.Dtos;

namespace Shop.Application.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> QueryAsync(ProductQueryParams query, CancellationToken ct = default);
    Task<IReadOnlyList<FacetDto>> GetFacetsAsync(string? category, CancellationToken ct = default);
    Task<ProductDetailDto> GetByIdAsync(Guid id, bool includeInactive = false, CancellationToken ct = default);
    Task<PagedResult<ProductDto>> ListAdminAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
