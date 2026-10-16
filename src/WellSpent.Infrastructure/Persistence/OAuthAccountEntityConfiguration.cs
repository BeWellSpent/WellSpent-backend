using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `oauth_account` table.</summary>
public sealed class OAuthAccountEntityConfiguration : IEntityTypeConfiguration<OAuthAccount>
{
    public void Configure(EntityTypeBuilder<OAuthAccount> b)
    {
        b.ToTable("oauth_account");
        b.HasKey(o => o.Id);

        b.Property(o => o.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(o => o.UserId).HasColumnName("user_id");
        b.Property(o => o.OauthName).HasColumnName("oauth_name").IsRequired();
        b.Property(o => o.AccountId).HasColumnName("account_id").IsRequired();
        b.Property(o => o.AccountEmail).HasColumnName("account_email").IsRequired();
        b.Property(o => o.RefreshToken).HasColumnName("refresh_token");
    }
}
