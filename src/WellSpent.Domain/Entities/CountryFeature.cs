namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `country_features` table. Composite key (CountryCode, FeatureName).</summary>
public sealed class CountryFeature
{
    public required string CountryCode { get; set; }
    public required string FeatureName { get; set; }
    public bool IsEnabled { get; set; } = true;
}
