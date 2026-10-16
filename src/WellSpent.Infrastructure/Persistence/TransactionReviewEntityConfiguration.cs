using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class TransactionReviewEntityConfiguration : IEntityTypeConfiguration<TransactionReview>
{
    public void Configure(EntityTypeBuilder<TransactionReview> b)
    {
        b.ToTable("transaction_review");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.BudgetPeriodId).HasColumnName("budget_period_id");
        b.Property(x => x.TransactionId).HasColumnName("transaction_id");
        b.Property(x => x.MatchedTransactionId).HasColumnName("matched_transaction_id");
        b.Property(x => x.MatchScore).HasColumnName("match_score").HasColumnType("numeric(5,2)");
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
