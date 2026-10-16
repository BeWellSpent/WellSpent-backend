using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>Mirrors Go's ListTransactionReviewsRow — the denormalized join (both transaction sides' name/amount, resolved person ids) ListAsync needs for display, kept here rather than on the entity since it isn't stored.</summary>
public sealed record TransactionReviewListRow(
    Guid Id, Guid BudgetPeriodId, Guid TransactionId, Guid MatchedTransactionId,
    decimal MatchScore, string Status, DateTime CreatedAt,
    string? TransactionName, decimal TransactionAmount, string? MatchedTransactionName,
    int? TransactionPersonId, int? MatchedTransactionPersonId);

public interface ITransactionReviewRepository
{
    /// <summary>Inserts a pending review, or — on a collision with the UNIQUE transaction_id — overwrites the matched transaction, score, and resets status to pending.</summary>
    Task<TransactionReview> UpsertAsync(Guid budgetPeriodId, Guid transactionId, Guid matchedTransactionId, decimal score, CancellationToken ct);

    /// <summary>Pending and confirmed reviews for a budget (never dismissed). Confirmed reviews return for every period, archived included — pending ones only for a live period.</summary>
    Task<List<TransactionReviewListRow>> ListAsync(Guid budgetProfileId, CancellationToken ct);

    Task<TransactionReview> GetByIdAsync(Guid id, CancellationToken ct);

    Task UpdateStatusAsync(Guid id, string status, CancellationToken ct);

    /// <summary>Null when the transaction has no review yet — a normal case, not an error.</summary>
    Task<TransactionReview?> GetByTransactionIdAsync(Guid transactionId, CancellationToken ct);

    /// <summary>A Fixed-type transaction can be matched by more than one variable transaction (e.g. a split bank transfer), so this is never assumed to be a single row.</summary>
    Task<List<TransactionReview>> ListByMatchedTransactionIdAsync(Guid matchedTransactionId, CancellationToken ct);

    /// <summary>Deletes only the review row, never the transactions it links. Scoped to 'pending' so an edit can never silently discard a confirmed or dismissed decision the user already made.</summary>
    Task DeleteIfPendingAsync(Guid id, CancellationToken ct);

    Task UpdateScoreIfPendingAsync(Guid id, decimal score, CancellationToken ct);

    Task ResetConfirmedByMatchedTransactionAsync(Guid matchedTransactionId, CancellationToken ct);

    Task CreateAliasAsync(Guid fixedExpenseId, string alias, CancellationToken ct);

    Task DeleteAliasAsync(Guid fixedExpenseId, string alias, CancellationToken ct);

    Task<List<string>> ListAliasesAsync(Guid fixedExpenseId, CancellationToken ct);
}
