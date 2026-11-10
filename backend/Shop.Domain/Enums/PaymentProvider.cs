namespace Shop.Domain.Enums;

/// <summary>B5/ADR-0003: which PSP fronted the pay page. FakePsp in dev/CI (this milestone), Stripe at milestone 13.</summary>
public enum PaymentProvider
{
    FakePsp = 0,
    Stripe = 1,
}
