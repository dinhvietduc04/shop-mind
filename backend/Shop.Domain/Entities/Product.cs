using Shop.Domain.Enums;

namespace Shop.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public decimal Price { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Category? Category { get; set; }
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public Inventory? Inventory { get; set; }

    public bool IsPurchasable => Status == ProductStatus.Active;
}
