using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetPeriodPlanEntityConfiguration : IEntityTypeConfiguration<BudgetPeriodPlan>
{
    public void Configure(EntityTypeBuilder<BudgetPeriodPlan> b)
    {
        b.ToTable("budget_period_plan");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetPeriodId).HasColumnName("budget_period_id");
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.PlannedAmount).HasColumnName("planned_amount").HasColumnType("numeric(15,4)");
    }
}
