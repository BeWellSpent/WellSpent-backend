using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `device_token` table.</summary>
public sealed class DeviceTokenEntityConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_token");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Platform).HasColumnName("platform").IsRequired();
        b.Property(x => x.Token).HasColumnName("token").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();

        b.HasIndex(x => x.Token).IsUnique();
    }
}
