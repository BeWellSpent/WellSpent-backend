using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// Amount-based remaining-balance math for a fixed expense's payment plan,
/// and the shared "did this payment just finish the plan" check (issue #55).
/// Deliberately separate from FixedExpenseScheduling, which is pure date
/// arithmetic and has no idea whether a payment actually happened, was late,
/// or was for more than planned.
/// </summary>
public static class FixedExpensePayoff
{
    /// <summary>TotalPayments x PlannedAmount minus amountPaid, clamped to zero. Zero when TotalPayments is unset — ignores date/count history entirely.</summary>
    public static decimal RemainingBalance(FixedExpense fe, decimal amountPaid) =>
        fe.TotalPayments is { } total && total > 0 ? Math.Max(0m, total * fe.PlannedAmount - amountPaid) : 0m;

    /// <summary>Skips the DB round-trip when TotalPayments is unset — RemainingBalance is zero regardless.</summary>
    public static Task<decimal> AmountPaidAsync(IFixedExpenseRepository repo, FixedExpense fe, CancellationToken ct) =>
        fe.TotalPayments is { } total && total > 0 ? repo.SumPaidAmountAsync(fe.Id, ct) : Task.FromResult(0m);

    /// <summary>
    /// Runs after any payment against a FixedExpense with TotalPayments set.
    /// If the payment just brought RemainingBalance to zero: deactivates the
    /// template and stamps PaidOffDate (EndDate itself never moves), and —
    /// only when that happened strictly before EndDate — flags tx
    /// FixedExpensePaidOffEarly. Shared by every mark-paid path and the
    /// pay-in-full command, so completion is detected the same way no matter
    /// which one crossed the threshold. Never throws past a bookkeeping
    /// failure — the payment itself already stands.
    /// </summary>
    public static async Task CheckAndApplyAsync(
        IFixedExpenseRepository fixedExpenses, ITransactionRepository transactions,
        Transaction tx, FixedExpense fe, DateOnly paidDate, ILogger logger, CancellationToken ct)
    {
        if (fe.TotalPayments is not { } total || total <= 0 || !fe.IsActive || fe.PaidOffDate is not null)
        {
            return;
        }

        try
        {
            var amountPaid = await fixedExpenses.SumPaidAmountAsync(fe.Id, ct);
            if (RemainingBalance(fe, amountPaid) > 0)
            {
                return;
            }

            await fixedExpenses.SetPaidOffAsync(fe.Id, fe.BudgetProfileId, paidDate, ct);
            // SetPaidOffAsync writes via ExecuteUpdateAsync, which bypasses EF's
            // change tracker — a caller still holding this same tracked fe (or
            // re-fetching it within the same DbContext/request) would otherwise
            // see stale IsActive/PaidOffDate. Mirrors the tx mutation below.
            fe.IsActive = false;
            fe.PaidOffDate = paidDate;

            var early = fe.EndDate is { } endDate && paidDate < endDate;
            if (early)
            {
                await transactions.SetFixedExpensePaidOffEarlyAsync(tx.Id, ct);
                tx.FixedExpensePaidOffEarly = true;
            }

            logger.LogInformation(
                "fixed_expense.paid_off fixed_expense_id={FixedExpenseId} transaction_id={TransactionId} paid_date={PaidDate} early={Early}",
                fe.Id, tx.Id, paidDate, early);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "fixed_expense.payoff_check_failed fixed_expense_id={FixedExpenseId} transaction_id={TransactionId}", fe.Id, tx.Id);
        }
    }
}
