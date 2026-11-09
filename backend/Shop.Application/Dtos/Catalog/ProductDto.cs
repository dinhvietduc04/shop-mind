using Shop.Domain.Enums;

namespace Shop.Application.Dtos;

public record ProductDto(
    Guid Id,
    string Name,
    string Slug,
    string SKU,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    Guid CategoryId,
    string? CategoryName,
    int AvailableStock,
    IReadOnlyList<ProductImageDto> Images);
