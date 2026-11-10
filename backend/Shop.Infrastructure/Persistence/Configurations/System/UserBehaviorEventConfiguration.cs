using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Entities;

namespace Shop.Infrastructure.Persistence.Configurations;

/// <summary>B4 analytics sink per the M18 schema (indexes per plan).</summary>
public class UserBehaviorEventConfiguration : IEntityTypeConfiguration<UserBehaviorEvent>
{
    public void Configure(EntityTypeBuilder<UserBehaviorEvent> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).IsRequired().HasMaxLength(64);
        b.Property(x => x.QueryText).HasMaxLength(500);
        b.Property(x => x.MetadataJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
    }
}
