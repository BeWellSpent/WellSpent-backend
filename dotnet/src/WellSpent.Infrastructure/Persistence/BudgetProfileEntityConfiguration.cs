using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps the minimal slice of `budget_profile` this sub-issue needs — see BudgetProfile's doc comment.</summary>
public sealed class BudgetProfileEntityConfiguration : IEntityTypeConfiguration<BudgetProfile>
{
    public void Configure(EntityTypeBuilder<BudgetProfile> b)
    {
        b.ToTable("budget_profile");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();

        // This entity is intentionally read-mostly for B4 (no Create/Update
        // here yet), so unmapped columns (cycle, country_code, etc.) don't
        // need placeholders — EF Core only needs to know about columns this
        // entity actually declares.
    }
}
