using Shop.Domain.Entities;
using Shop.Domain.Exceptions;

namespace Shop.UnitTests;

public class InventoryTests
{
    [Fact]
    public void AvailableQuantity_IsQuantityMinusReserved()
    {
        var inv = new Inventory { Quantity = 10, ReservedQuantity = 3 };
        Assert.Equal(7, inv.AvailableQuantity);
    }

    [Fact]
    public void DecreaseStock_ReducesQuantity()
    {
        var inv = new Inventory { ProductId = Guid.NewGuid(), Quantity = 5, ReservedQuantity = 0 };
        inv.DecreaseStock(3);
        Assert.Equal(2, inv.Quantity);
    }

    [Fact]
    public void DecreaseStock_MoreThanAvailable_Throws()
    {
        var inv = new Inventory { ProductId = Guid.NewGuid(), Quantity = 5, ReservedQuantity = 0 };
        Assert.Throws<InsufficientStockException>(() => inv.DecreaseStock(6));
    }

    [Fact]
    public void SetQuantity_Negative_Throws()
    {
        var inv = new Inventory { Quantity = 5, ReservedQuantity = 0 };
        Assert.Throws<DomainValidationException>(() => inv.SetQuantity(-1));
    }

    [Fact]
    public void SetQuantity_LessThanReserved_Throws()
    {
        var inv = new Inventory { Quantity = 5, ReservedQuantity = 3 };
        Assert.Throws<DomainValidationException>(() => inv.SetQuantity(2));
    }

    [Fact]
    public void Validate_ReservedExceedsQuantity_Throws()
    {
        var inv = new Inventory { Quantity = 2, ReservedQuantity = 5 };
        Assert.Throws<DomainValidationException>(() => inv.Validate());
    }
}
