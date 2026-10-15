using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class ExpenseAllocationEntityConfiguration : IEntityTypeConfiguration<ExpenseAllocation>
{
    public void Configure(EntityTypeBuilder<ExpenseAllocation> b)
    {
        b.ToTable("expense_allocation");
        b.HasKey(x => x.Id);

        // SERIAL (int4) PK — EF Core's Npgsql provider defaults a non-Guid
        // key to ValueGeneratedOnAdd automatically.
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.PlannedAmount).HasColumnName("planned_amount").HasColumnType("numeric");
    }
}
