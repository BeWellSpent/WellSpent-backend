using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.TransactionReviews;

/// <summary>
/// fixedExpenseId/fixedExpenseName are deliberately absent — deprecated on
/// the wire, superseded by matchedTransactionId/matchedTransactionName, no
/// longer populated by Go either.
/// </summary>
public sealed record TransactionReviewDto(
    Guid Id, Guid BudgetPeriodId, Guid TransactionId, Guid MatchedTransactionId,
    double MatchScore, string Status, DateTime CreatedAt,
    string? TransactionName, Money TransactionAmount, string? MatchedTransactionName,
    long TransactionPersonId, long MatchedTransactionPersonId);

public static class TransactionReviewMapping
{
    public static TransactionReviewDto ToDto(TransactionReviewListRow row) => new(
        row.Id, row.BudgetPeriodId, row.TransactionId, row.MatchedTransactionId,
        (double)row.MatchScore, row.Status, row.CreatedAt,
        row.TransactionName, Money.FromDecimal(row.TransactionAmount), row.MatchedTransactionName,
        row.TransactionPersonId ?? 0, row.MatchedTransactionPersonId ?? 0);

    /// <summary>Used by MarkTransactionForReview, which returns the bare entity (Upsert's RETURNING), not the denormalized list row — person ids and display names are 0/null, matching Go's own MarkTransactionForReviewResponse (built straight from the Upsert result, no re-join).</summary>
    public static TransactionReviewDto ToDto(TransactionReview review) => new(
        review.Id, review.BudgetPeriodId, review.TransactionId, review.MatchedTransactionId,
        (double)review.MatchScore, review.Status, review.CreatedAt,
        null, Money.FromDecimal(0), null, 0, 0);
}
