namespace Shop.Application.Dtos;

public record CheckoutRequest(ShippingAddressDto ShippingAddress, string PaymentMethod = "fake", bool SimulateFailure = false, bool SimulateTimeout = false);
