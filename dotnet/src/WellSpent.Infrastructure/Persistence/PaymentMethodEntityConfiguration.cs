using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class PaymentMethodEntityConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("payment_methods");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.PaymentTypeId).HasColumnName("payment_type_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.BudgetPersonId).HasColumnName("budget_person_id");
        b.Property(x => x.Color).HasColumnName("color");
        b.Property(x => x.Alias).HasColumnName("alias");
        b.Property(x => x.IsActive).HasColumnName("is_active");
    }
}
