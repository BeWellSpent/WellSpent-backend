namespace WellSpent.Domain.Entities;

/// <summary>A per-person planned spending amount for a category. Profile-scoped; carries forward across periods. `Id` is the table's SERIAL (int4) PK.</summary>
public sealed class ExpenseAllocation
{
    public int Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public int CategoryId { get; set; }

    /// <summary>Null = unattributed.</summary>
    public int? BudgetPersonId { get; set; }

    public decimal PlannedAmount { get; set; }
}
