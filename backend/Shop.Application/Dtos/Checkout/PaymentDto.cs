namespace Shop.Application.Dtos;

public record PaymentDto(Guid Id, decimal Amount, string Status, string? TransactionId);
