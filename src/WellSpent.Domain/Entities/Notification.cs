namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `notification` table — an in-app notification delivered to a user.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BudgetProfileId { get; set; }

    /// <summary>Matches AlertSubscription.AlertType values.</summary>
    public required string AlertType { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
