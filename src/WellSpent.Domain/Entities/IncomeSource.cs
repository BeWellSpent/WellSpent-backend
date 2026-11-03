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

    /// <summary>Scheduled pay-day anchor. NULL = no regular schedule (irregular income).</summary>
    public DateOnly? AnchorDate { get; set; }

    /// <summary>Days before AnchorDate a deposit still counts as on-time for period funding. 0 = no tolerance.</summary>
    public int EarlyArrivalToleranceDays { get; set; }

    /// <summary>When this schedule version takes effect. NULL = effective since creation.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>When this schedule version stops being current. NULL = currently effective.</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Monthly/yearly anchor landing on a weekend rolls back to the preceding Friday. Meaningless for weekly/bi-weekly.</summary>
    public bool AdjustForWeekends { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
