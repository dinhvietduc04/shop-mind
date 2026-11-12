namespace Shop.Application.Pricing;

/// <summary>
/// M5/M13: the one shipping rule shared by every money path. A quote and its
/// side-effect-free preview must never disagree about the bill.
/// </summary>
public static class CheckoutPricing
{
    public const decimal FreeShippingThreshold = 500m;
    public const decimal FlatShippingFee = 15m;

    public static decimal ShippingFeeFor(decimal subtotal) =>
        subtotal >= FreeShippingThreshold || subtotal == 0m ? 0m : FlatShippingFee;

    public static decimal TotalFor(decimal subtotal) => subtotal + ShippingFeeFor(subtotal);
}
