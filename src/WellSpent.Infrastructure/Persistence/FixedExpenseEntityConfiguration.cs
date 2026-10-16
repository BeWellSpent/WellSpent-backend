using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class FixedExpenseEntityConfiguration : IEntityTypeConfiguration<FixedExpense>
{
    public void Configure(EntityTypeBuilder<FixedExpense> b)
    {
        b.ToTable("fixed_expense");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.PlannedAmount).HasColumnName("planned_amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.PaymentMethodId).HasColumnName("payment_method_id");
        b.Property(x => x.DayOfMonth).HasColumnName("day_of_month");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
        b.Property(x => x.IntervalMonths).HasColumnName("interval_months");
        b.Property(x => x.AnchorDate).HasColumnName("anchor_date");
        b.Property(x => x.FrequencyUnit).HasColumnName("frequency_unit");
        b.Property(x => x.IntervalWeeks).HasColumnName("interval_weeks");
        b.Property(x => x.DayOfWeek).HasColumnName("day_of_week");
        b.Property(x => x.EndDate).HasColumnName("end_date");
        b.Property(x => x.TotalPayments).HasColumnName("total_payments");
        b.Property(x => x.IsInstallmentPlan).HasColumnName("is_installment_plan");
    }
}
