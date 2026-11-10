using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

/// <summary>B4: (EventId, Consumer) composite PK — the exactly-once ledger.</summary>
public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> b)
    {
        b.HasKey(x => new { x.EventId, x.Consumer });
        b.Property(x => x.Consumer).HasMaxLength(100);
    }
}
