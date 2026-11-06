namespace Shop.Domain.Entities;

public class Cart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    public int TotalQuantity => Items.Sum(i => i.Quantity);
}
