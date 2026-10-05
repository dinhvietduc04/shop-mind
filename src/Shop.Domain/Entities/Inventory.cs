using Shop.Domain.Exceptions;

namespace Shop.Domain.Entities;

public class Inventory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Product? Product { get; set; }

    public int AvailableQuantity => Quantity - ReservedQuantity;

    public void Validate()
    {
        if (Quantity < 0)
            throw new DomainValidationException("Quantity cannot be negative.");
        if (ReservedQuantity < 0)
            throw new DomainValidationException("ReservedQuantity cannot be negative.");
        if (ReservedQuantity > Quantity)
            throw new DomainValidationException("ReservedQuantity cannot exceed Quantity.");
    }

    public void DecreaseStock(int amount)
    {
        if (amount <= 0)
            throw new DomainValidationException("Amount must be greater than zero.");
        if (AvailableQuantity < amount)
            throw new InsufficientStockException(ProductId, AvailableQuantity, amount);
        Quantity -= amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetQuantity(int quantity)
    {
        if (quantity < 0)
            throw new DomainValidationException("Quantity cannot be negative.");
        if (quantity < ReservedQuantity)
            throw new DomainValidationException("Quantity cannot be less than reserved quantity.");
        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }
}
