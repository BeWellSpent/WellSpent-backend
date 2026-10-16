namespace WellSpent.Domain.Entities;

/// <summary>A merchant name observed to match a FixedExpense template, saved on confirm so future imports of the same name auto-confirm. `Id` is the table's SERIAL (int4) PK.</summary>
public sealed class FixedExpenseAlias
{
    public int Id { get; set; }
    public Guid FixedExpenseId { get; set; }
    public required string Alias { get; set; }
    public DateTime CreatedAt { get; set; }
}
