using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `changelog_release` table. UNIQUE (component, version) is enforced by the DB, not duplicated here.</summary>
public sealed class ChangelogReleaseEntityConfiguration : IEntityTypeConfiguration<ChangelogRelease>
{
    public void Configure(EntityTypeBuilder<ChangelogRelease> b)
    {
        b.ToTable("changelog_release");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.Component).HasColumnName("component").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.ReleasedAt).HasColumnName("released_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();

        b.HasMany(x => x.Items)
            .WithOne(i => i.Release)
            .HasForeignKey(i => i.ReleaseId);
    }
}
