using Shop.Domain.Entities;

namespace Shop.UnitTests;

public class CartTests
{
    [Fact]
    public void Subtotal_SumsItems()
    {
        var cart = new Cart
        {
            Items = new List<CartItem>
            {
                new() { Quantity = 2, UnitPrice = 100m },
                new() { Quantity = 1, UnitPrice = 50m },
            }
        };
        Assert.Equal(250m, cart.Subtotal);
        Assert.Equal(3, cart.TotalQuantity);
    }

    [Fact]
    public void CartItem_Subtotal_IsQuantityTimesPrice()
    {
        var item = new CartItem { Quantity = 3, UnitPrice = 19.99m };
        Assert.Equal(59.97m, item.Subtotal);
    }

    [Fact]
    public void GenerateOrderNumber_HasExpectedPrefix()
    {
        var n = Order.GenerateOrderNumber();
        Assert.StartsWith("SM-", n);
    }
}
