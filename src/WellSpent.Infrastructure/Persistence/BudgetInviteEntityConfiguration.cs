using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `budget_invite` table.</summary>
public sealed class BudgetInviteEntityConfiguration : IEntityTypeConfiguration<BudgetInvite>
{
    public void Configure(EntityTypeBuilder<BudgetInvite> b)
    {
        b.ToTable("budget_invite");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.Role).HasColumnName("role").IsRequired();
        b.Property(x => x.Token).HasColumnName("token")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.InvitedBy).HasColumnName("invited_by");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
