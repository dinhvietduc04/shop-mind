using Shop.Application.Pricing;

namespace Shop.UnitTests;

/// <summary>
/// M13: the preview and the quote must never disagree about shipping, so the
/// rule lives in one static policy instead of two copies.
/// </summary>
public class CheckoutPricingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(49.99, 15)]
    [InlineData(499.99, 15)]
    [InlineData(500, 0)]
    [InlineData(1499, 0)]
    public void Shipping_is_flat_below_the_threshold_and_free_at_or_above_it(
        double subtotal, double expected)
    {
        Assert.Equal((decimal)expected, CheckoutPricing.ShippingFeeFor((decimal)subtotal));
    }

    [Theory]
    [InlineData(49.99, 64.99)]
    [InlineData(500, 500)]
    public void Total_adds_the_shipping_rule_to_the_subtotal(double subtotal, double expected)
    {
        Assert.Equal((decimal)expected, CheckoutPricing.TotalFor((decimal)subtotal));
    }
}
