using Microsoft.Extensions.Logging;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.TransactionReviews;

/// <summary>
/// Marks the matched (fixed) side paid and excludes the transaction side —
/// the shared confirm path for ConfirmTransactionReview (the To Review tab)
/// and CreateFixedExpenseFromTransaction (which auto-confirms the match it
/// just created), mirroring Go's free function confirmTransactionMatch
/// exactly so both RPCs agree on what "confirmed" does.
/// </summary>
public static class TransactionReviewConfirmation
{
    public static async Task ConfirmTransactionMatchAsync(
        ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses,
        ITransactionReviewRepository reviews, IBudgetProfileRepository profiles,
        TransactionReview review, Guid budgetProfileId, ILogger logger, CancellationToken ct)
    {
        Transaction? matchedTx = await TryGetAsync(transactions, review.MatchedTransactionId, ct);

        if (matchedTx is not null)
        {
            Transaction? importedTx = await TryGetAsync(transactions, review.TransactionId, ct);

            // Save alias so future imports of the same merchant auto-confirm
            // — only meaningful when the match target was spawned from a
            // FixedExpense template; savings-derived transactions have no
            // template to alias against.
            if (importedTx?.Name is { } importedName && matchedTx.FixedExpenseId is { } feId)
            {
                try
                {
                    await reviews.CreateAliasAsync(feId, importedName, ct);
                }
                catch (Exception ex)
                {
                    // Not fatal: the alias only speeds up *future* imports of this merchant. This confirmation still stands.
                    logger.LogError(ex, "transaction.confirm_review: save alias {Alias} for fixed expense {FixedExpenseId}", importedName, feId);
                }
            }

            // Sums in any already-confirmed sibling reviews on this fixed transaction.
            if (matchedTx.BudgetPeriodId is { } matchedPeriodId)
            {
                List<TransactionReview> siblings;
                try
                {
                    siblings = await reviews.ListByMatchedTransactionIdAsync(review.MatchedTransactionId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "transaction.confirm_review: list sibling reviews for matched transaction {MatchedTransactionId}", review.MatchedTransactionId);
                    siblings = [];
                }

                var siblingAmounts = new List<decimal>();
                foreach (var sib in siblings)
                {
                    if (sib.Id == review.Id || sib.Status != "confirmed") continue;
                    var sibTx = await TryGetAsync(transactions, sib.TransactionId, ct);
                    if (sibTx is null)
                    {
                        logger.LogError("transaction.confirm_review: get sibling transaction {TransactionId}", sib.TransactionId);
                        continue;
                    }
                    siblingAmounts.Add(sibTx.Amount);
                }

                if (!matchedTx.IsPaid || siblingAmounts.Count > 0)
                {
                    var paidAmount = matchedTx.PlannedAmount;
                    var paidDate = matchedTx.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
                    var observed = default(ObservedPayment);
                    if (importedTx is not null)
                    {
                        paidAmount = importedTx.Amount;
                        paidDate = importedTx.Date ?? paidDate;
                        observed = new ObservedPayment(importedTx.CategoryId, importedTx.PaymentMethodId);
                    }
                    paidAmount = siblingAmounts.Sum() + paidAmount;

                    var autoSync = await FixedExpensePaymentSync.AutoUpdatePlannedAmountForAsync(profiles, budgetProfileId, ct);
                    // Deliberately not caught here, unlike the alias above:
                    // confirming a review whose whole point is "this bill was
                    // paid" must not report success when the bill is still
                    // unpaid. Nothing has been excluded and the review is
                    // still pending, so the user can retry.
                    await FixedExpensePaymentSync.MarkPaidAsync(
                        transactions, fixedExpenses, matchedTx.Id, matchedPeriodId, paidAmount, paidDate, autoSync, observed, ct);
                }
            }
        }

        // Exclude the imported variable transaction from totals — same
        // mechanism as an Income transaction — instead of hiding it from
        // ListTransactions entirely. It stays visible and toggleable, so
        // unmarking the matched fixed expense later (which resets this
        // review to pending) never leaves it stranded behind a
        // review-status side channel.
        try
        {
            await transactions.SetTransactionExcludedAsync(review.TransactionId, review.BudgetPeriodId, true, ct);
        }
        catch (Exception ex)
        {
            // Not fatal: the bill is paid and the link is about to be
            // recorded, so the match holds. The consequence is a duplicate
            // still counting toward totals, which the user can toggle off.
            logger.LogError(ex, "transaction.confirm_review: exclude imported transaction {TransactionId}", review.TransactionId);
        }

        await reviews.UpdateStatusAsync(review.Id, "confirmed", ct);
    }

    private static async Task<Transaction?> TryGetAsync(ITransactionRepository transactions, Guid id, CancellationToken ct)
    {
        try
        {
            return await transactions.GetTransactionAsync(id, ct);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
