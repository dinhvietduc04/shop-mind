using Shop.Application.Dtos;
using Shop.Application.Validation;

namespace Shop.UnitTests;

public class ValidatorTests
{
    [Fact]
    public async Task RegisterValidator_WeakPassword_Fails()
    {
        var v = new RegisterValidator();
        var r = await v.ValidateAsync(new RegisterRequest("a@b.com", "weak", "A", "B", null));
        Assert.False(r.IsValid);
    }

    [Fact]
    public async Task RegisterValidator_StrongPassword_Passes()
    {
        var v = new RegisterValidator();
        var r = await v.ValidateAsync(new RegisterRequest("a@b.com", "Strong123", "A", "B", null));
        Assert.True(r.IsValid);
    }

    [Fact]
    public async Task AddCartItemValidator_ZeroQuantity_Fails()
    {
        var v = new AddCartItemValidator();
        var r = await v.ValidateAsync(new AddCartItemRequest(Guid.NewGuid(), 0));
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.ErrorMessage.Contains("greater than zero"));
    }

    [Fact]
    public async Task CheckoutValidator_EmptyAddress_Fails()
    {
        var v = new CheckoutValidator();
        var r = await v.ValidateAsync(new CheckoutRequest(new ShippingAddressDto("", "", "", ""), "fake", false, false));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Slugify_Works()
    {
        Assert.Equal("thinkpad-x1", SlugHelper.Slugify("ThinkPad X1!!"));
    }
}
