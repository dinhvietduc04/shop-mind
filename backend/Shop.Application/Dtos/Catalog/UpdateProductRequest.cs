using Shop.Domain.Enums;

namespace Shop.Application.Dtos;

public record UpdateProductRequest(
    string Name,
    string? Slug,
    string SKU,
    Guid CategoryId,
    string? Description,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    List<CreateProductImageRequest>? Images);
