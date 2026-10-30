namespace WellSpent.Domain.Entities;

/// <summary>One cycle window of a BudgetProfile. Auto-created by the cycling job (B7); the first is created immediately when the profile is set up.</summary>
public sealed class BudgetPeriod
{
    public Guid Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>True right after a genuine cycle until the user confirms or retriggers the review.</summary>
    public bool NeedsReview { get; set; }
    public DateTime CreatedAt { get; set; }
}
