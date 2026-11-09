using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> b)
    {
        b.HasKey(x => x.Id);
        b.Property<uint>("xmin").IsRowVersion(); // B1
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
