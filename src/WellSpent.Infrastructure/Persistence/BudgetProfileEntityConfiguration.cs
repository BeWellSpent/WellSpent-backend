using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetProfileEntityConfiguration : IEntityTypeConfiguration<BudgetProfile>
{
    public void Configure(EntityTypeBuilder<BudgetProfile> b)
    {
        b.ToTable("budget_profile");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Cycle).HasColumnName("cycle").IsRequired();
        b.Property(x => x.CountryCode).HasColumnName("country_code");
        b.Property(x => x.CarryoverEnabled).HasColumnName("carryover_enabled");
        b.Property(x => x.AutoUpdatePlannedAmount).HasColumnName("auto_update_planned_amount");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
