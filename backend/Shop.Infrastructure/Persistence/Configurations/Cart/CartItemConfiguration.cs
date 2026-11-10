using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.HasKey(x => x.Id);
        b.Property<uint>("xmin").IsRowVersion(); // B1
        b.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.CartId);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
