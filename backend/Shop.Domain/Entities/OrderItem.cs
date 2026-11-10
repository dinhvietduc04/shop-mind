namespace Shop.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    /// <summary>B5: units of this line the quote still holds (decreases as commits/releases consume the reservation).</summary>
    public int ReservedQuantity { get; set; }

    public decimal Subtotal { get; set; }

    public Order? Order { get; set; }
}
