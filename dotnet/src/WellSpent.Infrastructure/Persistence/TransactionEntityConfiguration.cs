using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class TransactionEntityConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transaction");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.PlannedAmount).HasColumnName("planned_amount").HasColumnType("numeric(15,4)");
        b.Property(x => x.Date).HasColumnName("date");
        b.Property(x => x.RenewalDate).HasColumnName("renewal_date");
        b.Property(x => x.BudgetPeriodId).HasColumnName("budget_period_id");
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.PaymentMethodId).HasColumnName("payment_method_id");
        b.Property(x => x.TransactionFrequencyId).HasColumnName("transaction_frequency_id");
        b.Property(x => x.TransactionTypeId).HasColumnName("transaction_type_id");
        b.Property(x => x.IsPaid).HasColumnName("is_paid");
        b.Property(x => x.PaidDate).HasColumnName("paid_date");
        b.Property(x => x.FixedExpenseId).HasColumnName("fixed_expense_id");
        b.Property(x => x.PlaidTransactionId).HasColumnName("plaid_transaction_id");
        b.Property(x => x.IsExcluded).HasColumnName("is_excluded");
        b.Property(x => x.InstallmentFixedExpenseId).HasColumnName("installment_fixed_expense_id");
        b.Property(x => x.CarriedFromBudgetPeriodId).HasColumnName("carried_from_budget_period_id");
        b.Property(x => x.PlaidPfcPrimary).HasColumnName("plaid_pfc_primary");
        b.Property(x => x.PlaidPfcDetailed).HasColumnName("plaid_pfc_detailed");
        b.Property(x => x.PlaidReferenceNumber).HasColumnName("plaid_reference_number");
        b.Property(x => x.PlaidPpdId).HasColumnName("plaid_ppd_id");
    }
}
