namespace WellSpent.Domain.Entities;

/// <summary>Profile-level template for a recurring fixed expense. Spawns one transaction per budget period via period rollover.</summary>
public sealed class FixedExpense
{
    public Guid Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public required string Name { get; set; }
    public decimal PlannedAmount { get; set; }
    public int? CategoryId { get; set; }
    public Guid? PaymentMethodId { get; set; }

    /// <summary>1-31; clamped to the last day of the month when spawning. Meaningful when FrequencyUnit is month-based; kept in sync with AnchorDate when one is set.</summary>
    public int DayOfMonth { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>1 = every month (default), 3 = quarterly, 12 = yearly, etc. Applies when FrequencyUnit = Month.</summary>
    public int IntervalMonths { get; set; } = 1;

    /// <summary>Explicit interval anchor + scheduling day source; overrides CreatedAt/DayOfMonth when set.</summary>
    public DateOnly? AnchorDate { get; set; }

    /// <summary>1 = Month (default), 2 = Week.</summary>
    public short FrequencyUnit { get; set; } = 1;

    public int IntervalWeeks { get; set; } = 1;

    /// <summary>1 = Monday .. 7 = Sunday (ISO 8601); applies when FrequencyUnit = Week.</summary>
    public short DayOfWeek { get; set; } = 1;

    /// <summary>When set, the expense auto-deactivates once a period starts after this date.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Informational only; EndDate is the authoritative stop condition.</summary>
    public int? TotalPayments { get; set; }

    /// <summary>True when created by CreateInstallmentPlan — excluded from Plaid review auto-matching.</summary>
    public bool IsInstallmentPlan { get; set; }
}
