using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

/// <summary>Shared transaction-spawning logic used by CreateFixedExpense (one new template, its own current period) and BudgetPeriodRollover (every active template, a fresh period).</summary>
public static class FixedExpenseSpawning
{
    private const int FixedTransactionTypeId = 1;

    /// <summary>
    /// Spawns one transaction for every due week (per IntervalWeeks/DayOfWeek)
    /// in [startDate, endDate) — a WEEK-unit expense can have several due
    /// occurrences inside one period, unlike MONTH-unit (at most one), so
    /// de-duplication is per exact date rather than per calendar month.
    /// </summary>
    public static async Task SpawnWeeklyOccurrencesAsync(
        ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses,
        FixedExpense fe, Guid periodId, DateOnly startDate, DateOnly endDate,
        HashSet<Guid>? activePaymentMethodIds, CancellationToken ct)
    {
        for (var ws = FixedExpenseScheduling.WeekStart(startDate); ws < endDate; ws = ws.AddDays(7))
        {
            if (!FixedExpenseScheduling.IsDueInWeek(fe, ws)) continue;
            var date = FixedExpenseScheduling.DateInWeek(fe, ws);
            if (date < startDate || date >= endDate) continue;
            if (await fixedExpenses.HasTransactionOnDateAsync(fe.Id, date, ct)) continue;

            await transactions.CreateTransactionAsync(new Transaction
            {
                Name = fe.Name,
                Amount = fe.PlannedAmount,
                PlannedAmount = fe.PlannedAmount,
                Date = date,
                BudgetPeriodId = periodId,
                CategoryId = fe.CategoryId,
                PaymentMethodId = LivePaymentMethod(fe.PaymentMethodId, activePaymentMethodIds),
                TransactionTypeId = FixedTransactionTypeId,
                FixedExpenseId = fe.Id,
            }, ct);
        }
    }

    /// <summary>
    /// Drops a template's payment method when it has been deactivated, so a
    /// spawned bill lands unattributed rather than on a removed account. Null
    /// activeSet means the caller didn't supply one — leave the template's own
    /// value alone rather than second-guessing it (CreateFixedExpense's own
    /// spawn uses the payment method from the request the user just
    /// submitted, so there's nothing to re-check).
    /// </summary>
    public static Guid? LivePaymentMethod(Guid? id, HashSet<Guid>? activeSet) =>
        id is null || activeSet is null || activeSet.Contains(id.Value) ? id : null;
}
