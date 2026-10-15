using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetPeriodEntityConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> b)
    {
        b.ToTable("budget_period");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.StartDate).HasColumnName("start_date");
        b.Property(x => x.EndDate).HasColumnName("end_date");
        b.Property(x => x.IsArchived).HasColumnName("is_archived");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
