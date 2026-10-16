using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `users` table (docs/specs/03-data-model.md) — column names/types are never to be changed here independent of a migration, since the Go backend reads the same table.</summary>
public sealed class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        // id and created_at are both DB-generated defaults (gen_random_uuid(),
        // NOW()) — ValueGeneratedOnAdd is what makes EF Core read the actual
        // generated value back via RETURNING after INSERT instead of leaving
        // the in-memory default (Guid.Empty / DateTime.MinValue) on the
        // entity it just handed back to the caller.
        b.Property(u => u.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(u => u.Email).HasColumnName("email").IsRequired();
        b.Property(u => u.HashedPassword).HasColumnName("hashed_password");
        b.Property(u => u.FirstName).HasColumnName("first_name");
        b.Property(u => u.LastName).HasColumnName("last_name");
        b.Property(u => u.IsActive).HasColumnName("is_active");
        b.Property(u => u.IsSuperuser).HasColumnName("is_superuser");
        b.Property(u => u.IsVerified).HasColumnName("is_verified");
        b.Property(u => u.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
        b.Property(u => u.CountryCode).HasColumnName("country_code");
        b.Property(u => u.StateCode).HasColumnName("state_code");
        b.Property(u => u.FilingStatus).HasColumnName("filing_status").IsRequired();
        b.Property(u => u.TaxPaymentFrequency).HasColumnName("tax_payment_frequency");
        b.Property(u => u.Language).HasColumnName("language");
        b.Property(u => u.Currency).HasColumnName("currency");
        b.Property(u => u.EmailVerificationToken).HasColumnName("email_verification_token");
        b.Property(u => u.EmailVerificationExpiresAt).HasColumnName("email_verification_expires_at");
        b.Property(u => u.EmailVerificationLastSentAt).HasColumnName("email_verification_last_sent_at");
        b.Property(u => u.Status).HasColumnName("status");
        b.Property(u => u.ActiveUntil).HasColumnName("active_until");
        b.Property(u => u.Plan).HasColumnName("plan");
        b.Property(u => u.AccountType).HasColumnName("account_type");

        b.HasMany(u => u.OAuthAccounts)
            .WithOne(o => o.User)
            .HasForeignKey(o => o.UserId);
    }
}
