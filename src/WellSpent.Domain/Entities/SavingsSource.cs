namespace WellSpent.Domain.Entities;

/// <summary>A planned savings allocation on the profile, carrying forward every period. `Id` is the table's SERIAL (int4) PK.</summary>
public sealed class SavingsSource
{
    public int Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public int? BudgetPersonId { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }

    /// <summary>"monthly" | "bi_weekly" | "weekly" — derived from PaymentDays.Count, never set directly by a caller.</summary>
    public string Frequency { get; set; } = "monthly";

    /// <summary>System-managed; at most one per person per budget (US only). Recalculated whenever income sources change.</summary>
    public bool IsTaxReserve { get; set; }

    public decimal? FederalAmount { get; set; }
    public decimal? StateAmount { get; set; }
    public Guid? PaymentMethodId { get; set; }

    /// <summary>Day-of-month for each scheduled transfer; count encodes frequency (1=monthly, 2=bi-weekly, 4=weekly).</summary>
    public int[] PaymentDays { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
