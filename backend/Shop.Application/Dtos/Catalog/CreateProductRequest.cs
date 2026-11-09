using Shop.Domain.Enums;

namespace Shop.Application.Dtos;

public record CreateProductRequest(
    string Name,
    string? Slug,
    string SKU,
    Guid CategoryId,
    string? Description,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    List<CreateProductImageRequest>? Images,
    int InitialStock);
