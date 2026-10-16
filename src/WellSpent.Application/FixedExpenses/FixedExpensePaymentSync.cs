using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// What was actually observed about a payment, for the two fields where "the
/// transaction being marked paid" and "the real-world transaction the
/// payment came from" can differ — confirming a match marks the *matched*
/// (fixed) transaction paid, but its category/payment method may not be what
/// was actually seen on the *imported* one. Null means "nothing observed":
/// the template keeps its existing value rather than being overwritten with
/// a guess. The plain manual button has no separate import to observe from,
/// so it always passes default and lets the paid transaction's own fields
/// speak for it instead.
/// </summary>
public readonly record struct ObservedPayment(int? CategoryId, Guid? PaymentMethodId);

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
        bool autoSyncTemplate, ObservedPayment observed, CancellationToken ct)
    {
        var tx = await transactions.MarkTransactionAsPaidAsync(id, budgetPeriodId, amount, paidDate, ct);

        if (!autoSyncTemplate || tx.FixedExpenseId is not { } feId)
        {
            return tx;
        }

        FixedExpense fe;
        try
        {
            fe = await fixedExpenses.GetByIdAsync(feId, ct);
        }
        catch (NotFoundException)
        {
            // Not fatal: the payment is recorded and correct. Only the
            // template missed the update — a wrong template, not a wrong
            // payment, and the user can edit it by hand.
            return tx;
        }

        // Prefer what was actually observed; otherwise whatever the
        // just-paid transaction itself carries; otherwise leave the
        // template's existing value rather than clearing a real one.
        var categoryId = observed.CategoryId ?? tx.CategoryId ?? fe.CategoryId;
        var paymentMethodId = observed.PaymentMethodId ?? tx.PaymentMethodId ?? fe.PaymentMethodId;
        var (dayOfMonth, dayOfWeek, anchorDate) = FixedExpenseScheduling.ScheduleFromAnchor(paidDate);

        try
        {
            await fixedExpenses.UpdateFromPaymentAsync(feId, amount, dayOfMonth, dayOfWeek, anchorDate, categoryId, paymentMethodId, ct);
        }
        catch (Exception)
        {
            // The template keeps its old plan/date/category/payment method while this period's payment stands regardless.
        }

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
