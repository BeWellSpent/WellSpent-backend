using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `changelog_item` table.</summary>
public sealed class ChangelogItemEntityConfiguration : IEntityTypeConfiguration<ChangelogItem>
{
    public void Configure(EntityTypeBuilder<ChangelogItem> b)
    {
        b.ToTable("changelog_item");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.ReleaseId).HasColumnName("release_id");
        b.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired();
        b.Property(x => x.SummaryEn).HasColumnName("summary_en").IsRequired();
        b.Property(x => x.SummaryEs).HasColumnName("summary_es").IsRequired();
        b.Property(x => x.Position).HasColumnName("position");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
