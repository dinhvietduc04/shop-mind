namespace Shop.Infrastructure.Payments;

/// <summary>B5: PSP adapter settings (dev secret; Stripe signing lands at milestone 13).</summary>
public sealed class PaymentsOptions
{
    public string FakePspSecret { get; set; } = "fake-psp-dev-secret-change-me";
}
