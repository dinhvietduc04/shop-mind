using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).IsRequired().HasMaxLength(256);
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).IsRequired();
        b.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
        b.Property(x => x.LastName).IsRequired().HasMaxLength(100);
        b.Property(x => x.PhoneNumber).HasMaxLength(32);
        b.Property(x => x.Role).IsRequired();
        b.HasOne(x => x.Cart).WithOne(c => c.User).HasForeignKey<Cart>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Orders).WithOne(o => o.User).HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Description).HasMaxLength(2000);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(300);
        b.Property(x => x.SKU).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.SKU).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Price).HasPrecision(18, 2);
        b.Property(x => x.Brand).HasMaxLength(200);
        b.HasIndex(x => x.CategoryId);
        b.HasIndex(x => x.Brand);
        b.HasIndex(x => x.Price);
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Images).WithOne(i => i.Product).HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Inventory).WithOne(i => i.Product).HasForeignKey<Inventory>(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Url).IsRequired().HasMaxLength(1000);
        b.Property(x => x.AltText).HasMaxLength(300);
        b.HasIndex(x => x.ProductId);
    }
}

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.ProductId).IsUnique();
        b.Property(x => x.Quantity).IsRequired();
        b.Property(x => x.ReservedQuantity).IsRequired();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Inventory_Quantity_NonNegative", "\"Quantity\" >= 0");
            t.HasCheckConstraint("CK_Inventory_Reserved_NonNegative", "\"ReservedQuantity\" >= 0");
            t.HasCheckConstraint("CK_Inventory_Reserved_Lte_Quantity", "\"ReservedQuantity\" <= \"Quantity\"");
        });
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasMany(x => x.Items).WithOne(i => i.Cart).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.UserId);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.CartId);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(50);
        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.ShippingFee).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.Status);
        b.HasMany(x => x.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Payment).WithOne(p => p.Order).HasForeignKey<Payment>(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.OwnsOne(x => x.ShippingAddress, sa =>
        {
            sa.Property(p => p.FullName).HasColumnName("ShippingFullName").IsRequired().HasMaxLength(200);
            sa.Property(p => p.Phone).HasColumnName("ShippingPhone").IsRequired().HasMaxLength(32);
            sa.Property(p => p.Address).HasColumnName("ShippingAddress").IsRequired().HasMaxLength(500);
            sa.Property(p => p.City).HasColumnName("ShippingCity").IsRequired().HasMaxLength(200);
        });
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).IsRequired().HasMaxLength(300);
        b.Property(x => x.SKU).IsRequired().HasMaxLength(100);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.HasIndex(x => x.OrderId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.OrderId).IsUnique();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.TransactionId).HasMaxLength(200);
    }
}
