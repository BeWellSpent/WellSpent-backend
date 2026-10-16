namespace WellSpent.Domain.Entities;

/// <summary>A Plaid-imported (or manually flagged) variable transaction scored as a likely duplicate of a Fixed-type transaction already in the period.</summary>
public sealed class TransactionReview
{
    public Guid Id { get; set; }
    public Guid BudgetPeriodId { get; set; }

    /// <summary>The imported/flagged variable transaction. UNIQUE — a transaction can have at most one review.</summary>
    public Guid TransactionId { get; set; }

    /// <summary>The Fixed-type transaction (spawned from a FixedExpense template or a SavingsSource) this one duplicates.</summary>
    public Guid MatchedTransactionId { get; set; }

    public decimal MatchScore { get; set; }

    /// <summary>"pending" | "confirmed" | "dismissed".</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }
}
