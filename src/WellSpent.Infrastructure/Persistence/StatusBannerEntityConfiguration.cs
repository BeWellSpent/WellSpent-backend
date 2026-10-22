using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure.Persistence.Json;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `status_banner` table.</summary>
public sealed class StatusBannerEntityConfiguration : IEntityTypeConfiguration<StatusBanner>
{
    public void Configure(EntityTypeBuilder<StatusBanner> b)
    {
        b.ToTable("status_banner");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.Severity).HasColumnName("severity").IsRequired();
        // First JSONB column in this repo (issue #89) — copy LocalizedTextConverter/Comparer for the next one.
        b.Property(x => x.Message).HasColumnName("message").HasColumnType("jsonb")
            .HasConversion(LocalizedTextConverter.Instance).Metadata.SetValueComparer(LocalizedTextConverter.Comparer);
        b.Property(x => x.StartsAt).HasColumnName("starts_at")
            .HasDefaultValueSql("NOW()");
        b.Property(x => x.EndsAt).HasColumnName("ends_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
