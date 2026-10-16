namespace WellSpent.Domain.Entities;

/// <summary>Actual income recorded for one period. Pre-filled from IncomeSource.DefaultAmount; the amount is the only editable field per period.</summary>
public sealed class IncomeEntry
{
    public int Id { get; set; }
    public Guid BudgetPeriodId { get; set; }
    public int? IncomeSourceId { get; set; }
    public int? BudgetPersonId { get; set; }

    /// <summary>Denormalized from the source for display.</summary>
    public string? Name { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
