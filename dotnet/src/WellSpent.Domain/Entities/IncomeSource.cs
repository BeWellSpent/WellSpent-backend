namespace WellSpent.Domain.Entities;

/// <summary>A recurring income definition belonging to a budget profile. `Id` is the table's SERIAL (int4) PK.</summary>
public sealed class IncomeSource
{
    public int Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public int? BudgetPersonId { get; set; }
    public required string Name { get; set; }

    /// <summary>"salary" | "hourly" | "freelance" | "contractor" | "investment" | "interest" | "one_time" | "gift" | "other".</summary>
    public string IncomeType { get; set; } = "other";

    public decimal DefaultAmount { get; set; }
    public bool Recurring { get; set; } = true;

    /// <summary>"monthly" | "weekly" | "bi_weekly" | "yearly" | "one_off".</summary>
    public string PaymentFrequency { get; set; } = "monthly";

    public bool BeforeTax { get; set; }
    public DateTime CreatedAt { get; set; }
}
