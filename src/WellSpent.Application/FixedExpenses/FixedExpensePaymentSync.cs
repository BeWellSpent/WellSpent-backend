using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// Marking a fixed transaction paid, in one place. Three call sites do this:
/// MarkTransactionAsPaid (the button), ConfirmTransactionReview (the To
/// Review tab) and CreateFixedExpenseFromTransaction (same confirm path) —
/// they used to disagree in Go (only the button rewrote the template), so
/// the same bill paid three different ways left the next period planned at
/// different figures. Mirrors Go's mark_paid.go exactly.
/// </summary>
public static class FixedExpensePaymentSync
{
    public static async Task<Transaction> MarkPaidAsync(
        ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses,
        Guid id, Guid budgetPeriodId, decimal amount, DateOnly paidDate,
        bool autoSyncTemplate, ILogger logger, CancellationToken ct)
    {
        var tx = await transactions.MarkTransactionAsPaidAsync(id, budgetPeriodId, amount, paidDate, ct);

        if (tx.FixedExpenseId is not { } feId)
        {
            return tx;
        }

        FixedExpense fe;
        try
        {
            fe = await fixedExpenses.GetByIdAsync(feId, ct);
        }
        catch (NotFoundException ex)
        {
            // Not fatal: the payment is recorded and correct. Only the
            // template missed the update — a wrong template, not a wrong
            // payment, and the user can edit it by hand.
            logger.LogError(ex, "fixed_expense.payment_sync_template_missing fixed_expense_id={FixedExpenseId} transaction_id={TransactionId}", feId, tx.Id);
            return tx;
        }

        // EndDate means a payment plan (same signal createNextPeriod uses) — an overpayment there is credit toward a fixed principal, never a new PlannedAmount.
        if (autoSyncTemplate && fe.EndDate is null)
        {
            // Only amount and due date follow a payment: category and payment method are the user's classification of the bill, not something a bank import gets to rewrite.
            var (dayOfMonth, dayOfWeek, anchorDate) = FixedExpenseScheduling.ScheduleFromAnchor(paidDate);

            try
            {
                await fixedExpenses.UpdateFromPaymentAsync(feId, amount, dayOfMonth, dayOfWeek, anchorDate, ct);
            }
            catch (Exception ex)
            {
                // The template keeps its old plan and date while this period's payment stands regardless.
                logger.LogError(ex, "fixed_expense.payment_sync_template_update_failed fixed_expense_id={FixedExpenseId} transaction_id={TransactionId}", feId, tx.Id);
            }
        }

        // Runs regardless of autoSyncTemplate — completion is detected no matter which mark-paid path crossed the threshold.
        await FixedExpensePayoff.CheckAndApplyAsync(fixedExpenses, transactions, tx, fe, paidDate, logger, ct);

        return tx;
    }

    /// <summary>Defaults to true when the profile can't be read, matching the column default and the behavior every path had before this setting existed: a failed lookup must not silently change what marking a bill paid does.</summary>
    public static async Task<bool> AutoUpdatePlannedAmountForAsync(IBudgetProfileRepository profiles, Guid budgetProfileId, CancellationToken ct)
    {
        try
        {
            var profile = await profiles.GetByIdAsync(budgetProfileId, ct);
            return profile.AutoUpdatePlannedAmount;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
