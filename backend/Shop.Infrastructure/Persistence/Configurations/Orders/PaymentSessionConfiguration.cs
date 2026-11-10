using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

/// <summary>B5: pay-page session — unique capability token, one session per quote order.</summary>
public class PaymentSessionConfiguration : IEntityTypeConfiguration<PaymentSession>
{
    public void Configure(EntityTypeBuilder<PaymentSession> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.SessionToken).IsRequired().HasMaxLength(64);
        b.HasIndex(x => x.SessionToken).IsUnique();
        b.HasIndex(x => x.OrderId).IsUnique(); // single-use: a new quote means a new order row
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(8);
        b.HasOne(x => x.Order).WithOne().HasForeignKey<PaymentSession>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>B5 (ADR-0005): append-only anomaly ledger.</summary>
public class PaymentAnomalyConfiguration : IEntityTypeConfiguration<PaymentAnomaly>
{
    public void Configure(EntityTypeBuilder<PaymentAnomaly> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.SessionToken).HasMaxLength(64);
        b.Property(x => x.ProviderEventId).IsRequired().HasMaxLength(200);
        b.Property(x => x.Observed).IsRequired().HasMaxLength(50);
        b.Property(x => x.CurrentStatus).IsRequired().HasMaxLength(50);
        b.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        b.HasIndex(x => x.OrderId);
    }
}
