using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

/// <summary>
/// M10: outbox table — jsonb payload, (Status, OccurredAt) index for the M11
/// publisher's poll query, smallint status per the plan.
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.AggregateType).IsRequired().HasMaxLength(50);
        b.Property(x => x.Type).IsRequired().HasMaxLength(100);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Status).HasConversion<short>();
        b.HasIndex(x => new { x.Status, x.OccurredAt });
    }
}
