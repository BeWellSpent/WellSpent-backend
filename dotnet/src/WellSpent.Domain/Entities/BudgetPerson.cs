namespace WellSpent.Domain.Entities;

/// <summary>
/// Deliberately minimal slice of the `budget_to_profile_mapping` table — just
/// the columns Notification/Invite's role checks and invite-acceptance writes
/// need (B4). B5 extends this same class with chart/review/focused-view
/// preferences etc. rather than redefining it. `Id` is the table's SERIAL
/// (int4) PK, matching the Go repository's int32 usage throughout.
/// </summary>
public sealed class BudgetPerson
{
    public int Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public string? UserName { get; set; }
    public Guid? UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "";

    /// <summary>"admin" | "collaborator" | "viewer" | "unspecified".</summary>
    public required string Role { get; set; }
}
