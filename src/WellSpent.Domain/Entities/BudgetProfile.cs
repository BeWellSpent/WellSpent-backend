namespace WellSpent.Domain.Entities;

public sealed class BudgetProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }

    /// <summary>"weekly" | "bi_weekly" | "monthly" | "yearly".</summary>
    public string Cycle { get; set; } = "monthly";

    /// <summary>Propagated from the owner at creation; ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    public bool CarryoverEnabled { get; set; }
    public bool AutoUpdatePlannedAmount { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
