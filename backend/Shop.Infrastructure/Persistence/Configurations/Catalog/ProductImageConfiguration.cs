using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

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
