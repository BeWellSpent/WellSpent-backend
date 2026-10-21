using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the new `refresh_token` table.</summary>
public sealed class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_token");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(r => r.UserId).HasColumnName("user_id");
        b.Property(r => r.DeviceId).HasColumnName("device_id").IsRequired();
        b.Property(r => r.TokenHash).HasColumnName("token_hash").IsRequired();
        b.Property(r => r.LifetimeSeconds).HasColumnName("lifetime_seconds");
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        b.Property(r => r.RevokedAt).HasColumnName("revoked_at");
        b.Property(r => r.LastUsedAt).HasColumnName("last_used_at");

        b.HasIndex(r => r.TokenHash).IsUnique();
        b.HasIndex(r => new { r.UserId, r.DeviceId });
        b.HasIndex(r => r.UserId);
    }
}
