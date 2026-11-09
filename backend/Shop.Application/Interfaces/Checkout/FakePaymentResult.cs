namespace Shop.Application.Interfaces;

public record FakePaymentResult(bool Succeeded, string TransactionId, string? FailureReason);
