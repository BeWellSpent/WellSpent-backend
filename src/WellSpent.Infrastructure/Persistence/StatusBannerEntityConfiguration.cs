using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

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
        b.Property(x => x.MessageEn).HasColumnName("message_en").IsRequired();
        b.Property(x => x.MessageEs).HasColumnName("message_es").IsRequired();
        b.Property(x => x.StartsAt).HasColumnName("starts_at")
            .HasDefaultValueSql("NOW()");
        b.Property(x => x.EndsAt).HasColumnName("ends_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
