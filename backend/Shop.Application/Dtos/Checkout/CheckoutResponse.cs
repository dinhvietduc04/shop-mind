namespace Shop.Application.Dtos;

public record CheckoutResponse(OrderDto Order, PaymentDto Payment);
