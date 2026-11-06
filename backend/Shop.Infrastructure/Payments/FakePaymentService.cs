using Shop.Application.Interfaces;

namespace Shop.Infrastructure.Payments;

public class FakePaymentService : IFakePaymentService
{
    public async Task<FakePaymentResult> ProcessAsync(Guid orderId, decimal amount, bool simulateFailure, bool simulateTimeout, CancellationToken ct = default)
    {
        if (simulateTimeout)
        {
            // Simulate a gateway timeout: delay then fail (V1 optional simulation)
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return new FakePaymentResult(false, $"FAKE-TIMEOUT-{Guid.NewGuid():N}", "Payment gateway timeout (simulated).");
        }
        if (simulateFailure)
        {
            await Task.Delay(200, ct);
            return new FakePaymentResult(false, $"FAKE-DECLINED-{Guid.NewGuid():N}", "Card declined (simulated).");
        }
        await Task.Delay(200, ct);
        return new FakePaymentResult(true, $"FAKE-{Guid.NewGuid():N}", null);
    }
}
