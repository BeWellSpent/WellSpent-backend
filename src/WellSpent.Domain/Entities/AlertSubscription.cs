namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `alert_subscription` table.</summary>
public sealed class AlertSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BudgetProfileId { get; set; }

    /// <summary>"new_transaction" | "spending_threshold" | "period_created" | "review_pending". Not validated against this set anywhere in the Go service either — mirrored as-is.</summary>
    public required string AlertType { get; set; }

    /// <summary>"email" | "in_app" | "both".</summary>
    public required string Channel { get; set; }

    /// <summary>0-100; only meaningful when AlertType == "spending_threshold".</summary>
    public decimal? ThresholdPct { get; set; }

    /// <summary>"budget" | "category"; only meaningful for spending_threshold.</summary>
    public string? ThresholdScope { get; set; }

    public int? CategoryId { get; set; }
    public bool NotifyAllMembers { get; set; }
    public DateTime CreatedAt { get; set; }
}
