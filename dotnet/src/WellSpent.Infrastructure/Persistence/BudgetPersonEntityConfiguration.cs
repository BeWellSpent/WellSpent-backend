using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps the minimal slice of `budget_to_profile_mapping` this sub-issue needs — see BudgetPerson's doc comment.</summary>
public sealed class BudgetPersonEntityConfiguration : IEntityTypeConfiguration<BudgetPerson>
{
    public void Configure(EntityTypeBuilder<BudgetPerson> b)
    {
        b.ToTable("budget_to_profile_mapping");
        b.HasKey(x => x.Id);

        // SERIAL (int4) PK — EF Core's Npgsql provider defaults a non-Guid
        // key to ValueGeneratedOnAdd automatically, which is exactly right
        // here (matches the column's GENERATED/SERIAL behavior).
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.UserName).HasColumnName("user_name");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.Color).HasColumnName("color");
        b.Property(x => x.Role).HasColumnName("role").IsRequired();
    }
}
