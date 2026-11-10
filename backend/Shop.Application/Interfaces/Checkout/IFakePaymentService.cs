namespace Shop.Application.Interfaces;

public interface IFakePaymentService
{
    Task<FakePaymentResult> ProcessAsync(Guid orderId, decimal amount, bool simulateFailure, bool simulateTimeout, CancellationToken ct = default);
}
