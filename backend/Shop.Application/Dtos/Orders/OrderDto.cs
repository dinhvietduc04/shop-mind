namespace Shop.Application.Dtos;

public record OrderDto(
    Guid Id,
    string OrderNumber,
    string Status,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalAmount,
    ShippingAddressDto ShippingAddress,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);
