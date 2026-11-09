using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

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
