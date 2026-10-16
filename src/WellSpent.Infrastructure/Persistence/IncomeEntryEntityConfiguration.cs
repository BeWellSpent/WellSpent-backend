using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class IncomeEntryEntityConfiguration : IEntityTypeConfiguration<IncomeEntry>
{
    public void Configure(EntityTypeBuilder<IncomeEntry> b)
    {
        b.ToTable("income_entry");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetPeriodId).HasColumnName("budget_period_id");
        b.Property(x => x.IncomeSourceId).HasColumnName("income_source_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
