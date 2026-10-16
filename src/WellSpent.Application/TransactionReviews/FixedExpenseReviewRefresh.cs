using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.TransactionReviews;

/// <summary>
/// After a FixedExpense template edit propagates onto its matched
/// transaction, re-scores every *pending* TransactionReview against it —
/// an edit that moves the amount/category/payment method far enough can
/// make a previously-matched import no longer a plausible duplicate, or
/// change its score. Mirrors Go's refreshReviewForMatchedTransaction/
/// refreshOnePendingReview, called from UpdateFixedExpense's own "sync
/// paid"/"sync unpaid" branches.
/// </summary>
public static class FixedExpenseReviewRefresh
{
    public static async Task RefreshForMatchedTransactionAsync(
        ITransactionReviewRepository reviews, ITransactionRepository transactions,
        Guid matchedTransactionId, FixedExpense fe, ILogger logger, CancellationToken ct)
    {
        List<TransactionReview> matchedReviews;
        try
        {
            matchedReviews = await reviews.ListByMatchedTransactionIdAsync(matchedTransactionId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "fixed_expense: list reviews for matched transaction {TransactionId}", matchedTransactionId);
            return;
        }

        foreach (var review in matchedReviews)
        {
            if (review.Status == "pending")
            {
                await RefreshOnePendingReviewAsync(reviews, transactions, review, fe, logger, ct);
            }
        }
    }

    private static async Task RefreshOnePendingReviewAsync(
        ITransactionReviewRepository reviews, ITransactionRepository transactions,
        TransactionReview review, FixedExpense fe, ILogger logger, CancellationToken ct)
    {
        Transaction variableTx;
        try
        {
            variableTx = await transactions.GetTransactionAsync(review.TransactionId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "fixed_expense: refresh review {ReviewId}: get transaction {TransactionId}", review.Id, review.TransactionId);
            return;
        }
        if (variableTx.Name is not { } name) return;

        var aliases = await TryListAliasesAsync(reviews, fe.Id, ct);
        var (score, bestFe) = TransactionMatching.ScoreBestMatch(
            name, variableTx.Amount, variableTx.CategoryId, variableTx.PaymentMethodId,
            [fe], new Dictionary<Guid, List<string>> { [fe.Id] = aliases });

        if (score < 80 || bestFe is null)
        {
            try
            {
                await reviews.DeleteIfPendingAsync(review.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "fixed_expense: remove stale review {ReviewId} after template edit", review.Id);
                return;
            }
            logger.LogInformation("fixed_expense: removed stale review {ReviewId} — fixed expense {FixedExpenseId} no longer matches (score={Score})", review.Id, fe.Id, score);
            return;
        }

        try
        {
            await reviews.UpdateScoreIfPendingAsync(review.Id, (decimal)score, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "fixed_expense: refresh review {ReviewId} score", review.Id);
            return;
        }
        logger.LogInformation("fixed_expense: refreshed review {ReviewId} score to {Score} after template edit", review.Id, score);
    }

    private static async Task<List<string>> TryListAliasesAsync(ITransactionReviewRepository reviews, Guid fixedExpenseId, CancellationToken ct)
    {
        try
        {
            return await reviews.ListAliasesAsync(fixedExpenseId, ct);
        }
        catch
        {
            return [];
        }
    }
}
