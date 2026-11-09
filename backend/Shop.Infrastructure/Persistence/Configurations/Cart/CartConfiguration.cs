using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.HasKey(x => x.Id);
        b.Property<uint>("xmin").IsRowVersion(); // B1
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasMany(x => x.Items).WithOne(i => i.Cart).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.UserId);
    }
}
