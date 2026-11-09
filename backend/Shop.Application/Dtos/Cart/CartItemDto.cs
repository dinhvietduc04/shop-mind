namespace Shop.Application.Dtos;

public record CartItemDto(Guid Id, Guid ProductId, string ProductName, string SKU, int Quantity, decimal UnitPrice, decimal Subtotal, string? ImageUrl);
