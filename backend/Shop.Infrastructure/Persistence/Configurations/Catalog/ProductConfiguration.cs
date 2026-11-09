using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.HasKey(x => x.Id);
        b.Property<uint>("xmin").IsRowVersion(); // B1: Postgres row version for optimistic concurrency
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
