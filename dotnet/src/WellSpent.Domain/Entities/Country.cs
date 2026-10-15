namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `countries` table.</summary>
public sealed class Country
{
    /// <summary>ISO 3166-1 alpha-2. Primary key.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
