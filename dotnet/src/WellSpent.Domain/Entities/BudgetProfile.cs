namespace WellSpent.Domain.Entities;

/// <summary>
/// Deliberately minimal slice of the `budget_profile` table — just enough for
/// Notification/Invite's membership and ownership checks (B4). B5 (the full
/// Budget domain) extends this same class with cycle, periods, income, etc.
/// rather than redefining it.
/// </summary>
public sealed class BudgetProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
}
