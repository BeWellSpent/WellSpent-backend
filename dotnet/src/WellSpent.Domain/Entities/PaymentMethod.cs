namespace WellSpent.Domain.Entities;

/// <summary>
/// Named instance of a payment type, attributed to a BudgetPerson. Deliberately
/// minimal slice for B5 batch 3 — Plaid linkage columns (plaid_account_id,
/// plaid_item_id) are added when B6 (Plaid domain) needs them, rather than now.
/// </summary>
public sealed class PaymentMethod
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int? PaymentTypeId { get; set; }
    public Guid? UserId { get; set; }
    public int? BudgetPersonId { get; set; }
    public string Color { get; set; } = "";

    /// <summary>User-defined display name; overrides Name in the UI when set. Null clears it.</summary>
    public string? Alias { get; set; }

    public bool IsActive { get; set; } = true;
}
