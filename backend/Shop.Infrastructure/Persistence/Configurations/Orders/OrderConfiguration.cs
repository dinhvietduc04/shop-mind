using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasKey(x => x.Id);
        b.Property<uint>("xmin").IsRowVersion(); // B1
        b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(50);
        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.ShippingFee).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Origin).HasConversion<string>().HasMaxLength(16)
            .HasDefaultValue(QuoteOrigin.Human);
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
