using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>Maps 1:1 to the existing `country_features` table.</summary>
public sealed class CountryFeatureEntityConfiguration : IEntityTypeConfiguration<CountryFeature>
{
    public void Configure(EntityTypeBuilder<CountryFeature> b)
    {
        b.ToTable("country_features");
        b.HasKey(x => new { x.CountryCode, x.FeatureName });

        b.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2);
        b.Property(x => x.FeatureName).HasColumnName("feature_name");
        b.Property(x => x.IsEnabled).HasColumnName("is_enabled");
    }
}
