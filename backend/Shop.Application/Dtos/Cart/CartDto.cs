namespace Shop.Application.Dtos;

public record CartDto(Guid Id, IReadOnlyList<CartItemDto> Items, decimal Subtotal, int TotalQuantity);
