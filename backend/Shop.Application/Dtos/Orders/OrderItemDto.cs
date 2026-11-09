namespace Shop.Application.Dtos;

public record OrderItemDto(Guid ProductId, string ProductName, string SKU, decimal UnitPrice, int Quantity, decimal Subtotal);
