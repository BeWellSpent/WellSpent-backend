using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `alert_subscription` table. The partial unique index on (user_id, budget_profile_id, alert_type, COALESCE(category_id,-1)) is enforced by the DB — see INotificationRepository.UpsertSubscriptionAsync's doc comment for how the app-level upsert relates to it.</summary>
public sealed class AlertSubscriptionEntityConfiguration : IEntityTypeConfiguration<AlertSubscription>
{
    public void Configure(EntityTypeBuilder<AlertSubscription> b)
    {
        b.ToTable("alert_subscription");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.AlertType).HasColumnName("alert_type").IsRequired();
        b.Property(x => x.Channel).HasColumnName("channel").IsRequired();
        b.Property(x => x.ThresholdPct).HasColumnName("threshold_pct");
        b.Property(x => x.ThresholdScope).HasColumnName("threshold_scope");
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.NotifyAllMembers).HasColumnName("notify_all_members");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
