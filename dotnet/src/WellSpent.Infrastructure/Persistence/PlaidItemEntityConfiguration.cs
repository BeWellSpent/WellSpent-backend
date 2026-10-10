using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class PlaidItemEntityConfiguration : IEntityTypeConfiguration<PlaidItem>
{
    public void Configure(EntityTypeBuilder<PlaidItem> b)
    {
        b.ToTable("plaid_item");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.BudgetProfileId).HasColumnName("budget_profile_id");
        b.Property(x => x.AccessToken).HasColumnName("access_token").IsRequired();
        b.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(x => x.InstitutionId).HasColumnName("institution_id");
        b.Property(x => x.InstitutionName).HasColumnName("institution_name");
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.Cursor).HasColumnName("cursor");
        b.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
        b.Property(x => x.LastManualResyncAt).HasColumnName("last_manual_resync_at");
    }
}
