namespace WellSpent.Domain.Entities;

/// <summary>Named instance of a payment type, attributed to a BudgetPerson.</summary>
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

    /// <summary>Plaid's own account id — dedup key across reconnects. Null for a manually-created method.</summary>
    public string? PlaidAccountId { get; set; }

    /// <summary>The PlaidItem this method was created from. Null for a manually-created method.</summary>
    public Guid? PlaidItemId { get; set; }
}
