namespace Shop.Application.Dtos;

public record AddCartItemRequest(Guid ProductId, int Quantity);
