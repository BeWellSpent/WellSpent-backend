namespace WellSpent.Domain.Entities;

/// <summary>A category's planned amount as it stood when its period closed. Null person = the whole category.</summary>
public sealed class BudgetPeriodPlan
{
    public long Id { get; set; }
    public Guid BudgetPeriodId { get; set; }
    public int CategoryId { get; set; }
    public int? BudgetPersonId { get; set; }
    public decimal PlannedAmount { get; set; }
}
