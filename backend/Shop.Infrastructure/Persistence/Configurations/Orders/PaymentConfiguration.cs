using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

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
