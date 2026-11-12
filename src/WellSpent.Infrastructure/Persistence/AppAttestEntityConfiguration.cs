using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class AppAttestKeyEntityConfiguration : IEntityTypeConfiguration<AppAttestKey>
{
    public void Configure(EntityTypeBuilder<AppAttestKey> b)
    {
        b.ToTable("app_attest_key");
        b.HasKey(x => x.KeyId);
        b.Property(x => x.KeyId).HasColumnName("key_id");
        b.Property(x => x.PublicKey).HasColumnName("public_key");
        b.Property(x => x.SignCount).HasColumnName("sign_count");
        b.Property(x => x.Environment).HasColumnName("environment");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
    }
}

public sealed class AppAttestChallengeEntityConfiguration : IEntityTypeConfiguration<AppAttestChallenge>
{
    public void Configure(EntityTypeBuilder<AppAttestChallenge> b)
    {
        b.ToTable("app_attest_challenge");
        b.HasKey(x => x.Challenge);
        b.Property(x => x.Challenge).HasColumnName("challenge");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.UsedAt).HasColumnName("used_at");
    }
}
