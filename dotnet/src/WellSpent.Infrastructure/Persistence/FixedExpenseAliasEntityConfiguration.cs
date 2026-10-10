using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class FixedExpenseAliasEntityConfiguration : IEntityTypeConfiguration<FixedExpenseAlias>
{
    public void Configure(EntityTypeBuilder<FixedExpenseAlias> b)
    {
        b.ToTable("fixed_expense_alias");
        b.HasKey(x => x.Id);

        // SERIAL (int4) PK — EF Core's Npgsql provider defaults a non-Guid
        // key to ValueGeneratedOnAdd automatically.
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.FixedExpenseId).HasColumnName("fixed_expense_id");
        b.Property(x => x.Alias).HasColumnName("alias").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
