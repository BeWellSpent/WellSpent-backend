using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `countries` table.</summary>
public sealed class CountryEntityConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> b)
    {
        b.ToTable("countries");
        b.HasKey(x => x.Code);

        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(2);
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }
}
