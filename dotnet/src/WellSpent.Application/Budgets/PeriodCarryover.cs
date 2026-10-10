using Microsoft.Extensions.Logging;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>I/O shell around Carryover: loads the closing period's data, calls the pure compute, writes the resulting rows. Ports Go's applyCarryover.</summary>
public static class PeriodCarryover
{
    private const int VariableTransactionTypeId = 2;

    /// <summary>No-op when CarryoverEnabled is off. Idempotent via CountCarriedAsync — the daily job and the client-callable RPC both reach this with no shared transaction wrapping.</summary>
    public static async Task ApplyAsync(
        IBudgetProfileRepository profiles, ITransactionRepository transactions, ILogger logger,
        BudgetProfile profile, BudgetPeriod closing, BudgetPeriod next, CancellationToken ct)
    {
        if (!profile.CarryoverEnabled) return;

        int carried;
        try { carried = await transactions.CountCarriedAsync(next.Id, closing.Id, ct); }
        catch (Exception ex)
        {
            logger.LogError(ex, "carryover.idempotency_check_failed closing_period_id={ClosingPeriodId} next_period_id={NextPeriodId}", closing.Id, next.Id);
            return;
        }
        if (carried > 0) return;

        Dictionary<string, int> systemCategories;
        try { systemCategories = await transactions.ListSystemCategoriesAsync(ct); }
        catch (Exception ex)
        {
            logger.LogError(ex, "carryover.list_system_categories_failed profile_id={ProfileId}", profile.Id);
            return;
        }

        List<Transaction> txs;
        List<IncomeEntry> incomeEntries;
        try
        {
            txs = await transactions.ListTransactionsAsync(closing.Id, null, null, null, ct);
            incomeEntries = await profiles.ListIncomeEntriesAsync(closing.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "carryover.load_closing_period_data_failed closing_period_id={ClosingPeriodId}", closing.Id);
            return;
        }

        var nonSpend = SpendFilter.NonSpendCategoryIds(systemCategories);
        var (remainder, spendByMethod, unattributed) = Carryover.Inputs(txs, incomeEntries, nonSpend);
        var rows = Carryover.Compute(remainder, spendByMethod, unattributed);
        if (rows.Count == 0) return;

        var txDate = next.StartDate;
        foreach (var row in rows)
        {
            if (!systemCategories.TryGetValue(row.CategoryKey, out var categoryId))
            {
                // Savings and Debt are both seeded system categories — this only fires on a database missing migration 000052.
                logger.LogError("carryover.system_category_missing category_key={CategoryKey} profile_id={ProfileId}", row.CategoryKey, profile.Id);
                continue;
            }
            try
            {
                await transactions.CreateTransactionAsync(new Transaction
                {
                    Name = TransactionName(row, closing),
                    Amount = row.Amount,
                    PlannedAmount = row.Amount,
                    Date = txDate,
                    BudgetPeriodId = next.Id,
                    CategoryId = categoryId,
                    PaymentMethodId = row.PaymentMethodId,
                    TransactionTypeId = VariableTransactionTypeId,
                    CarriedFromBudgetPeriodId = closing.Id,
                }, ct);
            }
            catch (Exception ex)
            {
                // Partial carryover: rows already written stay, so the carried total no longer matches the balance it came from.
                logger.LogError(ex, "carryover.create_row_failed category_key={CategoryKey} next_period_id={NextPeriodId} closing_period_id={ClosingPeriodId}", row.CategoryKey, next.Id, closing.Id);
            }
        }
    }

    /// <summary>Labels a carried row with the period it came from, so it reads as an explanation rather than a mystery charge.</summary>
    private static string TransactionName(CarryoverRow row, BudgetPeriod closing)
    {
        var label = closing.StartDate.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy");
        return row.CategoryKey == Carryover.SavingsSystemKey ? $"Left over from {label}" : $"Carried balance from {label}";
    }
}
