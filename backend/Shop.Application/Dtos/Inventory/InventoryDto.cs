namespace Shop.Application.Dtos;

public record InventoryDto(Guid ProductId, string ProductName, string SKU, int Quantity, int ReservedQuantity, int AvailableQuantity);
