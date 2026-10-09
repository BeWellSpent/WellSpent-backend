using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class IncomeSourceEntityConfiguration : IEntityTypeConfiguration<IncomeSource>
{
    public void Configure(EntityTypeBuilder<IncomeSource> b)
    {
        b.ToTable("income_source");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.IncomeType).HasColumnName("income_type").IsRequired();
        b.Property(x => x.DefaultAmount).HasColumnName("default_amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.Recurring).HasColumnName("recurring");
        b.Property(x => x.PaymentFrequency).HasColumnName("payment_frequency").IsRequired();
        b.Property(x => x.BeforeTax).HasColumnName("before_tax");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
