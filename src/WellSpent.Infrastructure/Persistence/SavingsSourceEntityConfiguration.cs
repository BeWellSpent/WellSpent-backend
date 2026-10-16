using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class SavingsSourceEntityConfiguration : IEntityTypeConfiguration<SavingsSource>
{
    public void Configure(EntityTypeBuilder<SavingsSource> b)
    {
        b.ToTable("savings_source");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.Frequency).HasColumnName("frequency").IsRequired();
        b.Property(x => x.IsTaxReserve).HasColumnName("is_tax_reserve");
        b.Property(x => x.FederalAmount).HasColumnName("federal_amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.StateAmount).HasColumnName("state_amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.PaymentMethodId).HasColumnName("payment_method_id");
        b.Property(x => x.PaymentDays).HasColumnName("payment_days");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
