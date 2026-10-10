using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.TransactionReviews;

/// <summary>
/// Scores a created-or-edited variable transaction against active fixed
/// expenses; >=80 queues a review, and a still-pending review that no longer
/// matches after an edit is removed (never the transactions it linked).
/// Best-effort, every failure logged and swallowed — mirrors Go's
/// maybeQueueReview exactly, including its fail-open posture on lookup
/// errors. Gated per-person (userId), not per-budget: needs their own
/// ManualMatchReviewEnabled plus a paid plan.
/// </summary>
public static class ManualMatchReview
{
    public static async Task MaybeQueueReviewAsync(
        Transaction tx, Guid periodId, Guid userId,
        IBudgetProfileRepository profiles, IFixedExpenseRepository fixedExpenses,
        ITransactionReviewRepository reviews, IUserRepository users, ILogger logger, CancellationToken ct)
    {
        if (tx.Name is null) return;

        BudgetPeriod period;
        try
        {
            period = await profiles.GetPeriodByIdAsync(periodId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "transaction.match: tx {TransactionId}: get period {PeriodId}", tx.Id, periodId);
            return;
        }

        try
        {
            var person = await profiles.GetPersonByUserIdAsync(period.BudgetProfileId, userId, ct);
            if (!person.ManualMatchReviewEnabled)
            {
                logger.LogInformation("transaction.match: tx {TransactionId}: skipped — user {UserId} has manual match review disabled", tx.Id, userId);
                return;
            }
        }
        catch (Exception ex)
        {
            // Failing open: a lookup error must not silently disable a feature the user turned on.
            logger.LogError(ex, "transaction.match: tx {TransactionId}: get person for user {UserId} (failing open)", tx.Id, userId);
        }

        try
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user.Plan == "free")
            {
                logger.LogInformation("transaction.match: tx {TransactionId}: skipped — user {UserId} is on the free plan", tx.Id, userId);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "transaction.match: tx {TransactionId}: get plan for user {UserId} (failing open)", tx.Id, userId);
        }

        // An edit re-runs this same scoring against a transaction that may
        // already carry a review from a previous add/edit. Confirmed and
        // dismissed are decisions the user already made — an edit must
        // never silently reopen either, only a still-pending review is
        // ours to update or remove.
        var existing = await reviews.GetByTransactionIdAsync(tx.Id, ct);
        if (existing is not null && existing.Status != "pending")
        {
            logger.LogInformation("transaction.match: tx {TransactionId}: skipped — existing review {ReviewId} is {Status}, not reopening", tx.Id, existing.Id, existing.Status);
            return;
        }

        async Task RemoveStaleAsync(double score)
        {
            if (existing is null) return;
            try
            {
                await reviews.DeleteIfPendingAsync(existing.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "transaction.match: tx {TransactionId}: remove stale review {ReviewId}", tx.Id, existing.Id);
                return;
            }
            logger.LogInformation("transaction.match: tx {TransactionId}: removed stale review {ReviewId} — no longer matches (score={Score})", tx.Id, existing.Id, score);
        }

        List<FixedExpense> fixedExpenseList;
        try
        {
            fixedExpenseList = await fixedExpenses.ListAsync(period.BudgetProfileId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "transaction.match: tx {TransactionId}: list fixed expenses for profile {ProfileId}", tx.Id, period.BudgetProfileId);
            return;
        }

        if (fixedExpenseList.Count == 0)
        {
            logger.LogInformation("transaction.match: tx {TransactionId}: skipped — profile {ProfileId} has no active fixed expenses", tx.Id, period.BudgetProfileId);
            await RemoveStaleAsync(0);
            return;
        }

        var aliasesByFixedExpenseId = new Dictionary<Guid, List<string>>();
        foreach (var fe in fixedExpenseList)
        {
            try
            {
                aliasesByFixedExpenseId[fe.Id] = await reviews.ListAliasesAsync(fe.Id, ct);
            }
            catch
            {
                aliasesByFixedExpenseId[fe.Id] = [];
            }
        }

        var (bestScore, bestFe) = TransactionMatching.ScoreBestMatch(
            tx.Name, tx.Amount, tx.CategoryId, tx.PaymentMethodId, fixedExpenseList, aliasesByFixedExpenseId);
        if (bestScore < 80 || bestFe is null)
        {
            logger.LogInformation("transaction.match: tx {TransactionId} {Name} ${Amount}: best score {Score} against {Count} fixed expenses — below threshold",
                tx.Id, tx.Name, tx.Amount, bestScore, fixedExpenseList.Count);
            await RemoveStaleAsync(bestScore);
            return;
        }

        // Same-period only, matching MarkTransactionForReview's guard — a
        // review linking two different periods can't be rendered by either client.
        var unpaid = await fixedExpenses.GetUnpaidTransactionInPeriodAsync(bestFe.Id, periodId, ct);
        if (unpaid is null)
        {
            logger.LogInformation("transaction.match: tx {TransactionId}: matched fixed expense {FixedExpenseId} (score={Score}) but no unpaid transaction in period {PeriodId}",
                tx.Id, bestFe.Id, bestScore, periodId);
            await RemoveStaleAsync(bestScore);
            return;
        }

        try
        {
            await reviews.UpsertAsync(periodId, tx.Id, unpaid.Id, (decimal)bestScore, ct);
        }
        catch (Exception ex)
        {
            // Not fatal: the transaction is created and correct, it just won't be offered for review.
            logger.LogError(ex, "transaction.match: tx {TransactionId}: queue review against {UnpaidId}", tx.Id, unpaid.Id);
            return;
        }
        logger.LogInformation("transaction.match: tx {TransactionId} {Name}: queued review against fixed expense {FixedExpenseName} (score={Score})",
            tx.Id, tx.Name, bestFe.Name, bestScore);

        // HOOK: notify budget members of a pending review (Notification domain's event-dispatch wiring isn't done yet).
    }
}
