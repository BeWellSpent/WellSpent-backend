using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>Ports Go's createSavingsTransactions and its DeleteSavingsSourceTransactions pairing — shared by Add/Update/DeleteSavingsSource and period rollover.</summary>
public static class SavingsTransactionSpawning
{
    private const int FixedTransactionTypeId = 1;
    private static readonly Dictionary<string, int> FrequencyToTransactionFrequencyId = new() { ["weekly"] = 2, ["bi_weekly"] = 3, ["monthly"] = 4 };

    /// <summary>Spawns one Fixed/Savings transaction per payment day in the profile's latest period. Every failure is swallowed except a missing period/category, matching Go's best-effort //nolint:errcheck.</summary>
    public static async Task SpawnAsync(
        IBudgetProfileRepository profiles, ITransactionRepository transactions, ILogger logger,
        Guid profileId, SavingsSource source, CancellationToken ct)
    {
        BudgetPeriod period;
        try { period = await profiles.GetLatestPeriodAsync(profileId, ct); }
        catch { return; }

        if (await ResolveSavingsCategoryIdAsync(transactions, logger, profileId, ct) is not { } savingsCategoryId) return;

        var txFrequencyId = FrequencyToTransactionFrequencyId.GetValueOrDefault(source.Frequency, 4);
        var perDayAmount = source.PaymentDays.Length > 1 ? source.Amount / source.PaymentDays.Length : source.Amount;
        var lastDay = DateTime.DaysInMonth(period.StartDate.Year, period.StartDate.Month);

        foreach (var day in source.PaymentDays)
        {
            try
            {
                await transactions.CreateTransactionAsync(new Transaction
                {
                    Name = source.Name,
                    Amount = perDayAmount,
                    PlannedAmount = perDayAmount,
                    Date = new DateOnly(period.StartDate.Year, period.StartDate.Month, Math.Min(day, lastDay)),
                    BudgetPeriodId = period.Id,
                    CategoryId = savingsCategoryId,
                    PaymentMethodId = source.PaymentMethodId,
                    TransactionFrequencyId = txFrequencyId,
                    TransactionTypeId = FixedTransactionTypeId,
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "savings.create_transaction_failed savings_source_id={SavingsSourceId} period_id={PeriodId} day={Day}", source.Id, period.Id, day);
            }
        }
    }

    /// <summary>No-op when the source has no payment method — matches Go's `if old.PaymentMethodID != nil` guard (a method-less source has no auto-created transactions to find).</summary>
    public static async Task DeleteExistingTransactionsAsync(
        ITransactionRepository transactions, ILogger logger, Guid profileId, SavingsSource source, CancellationToken ct)
    {
        if (source.PaymentMethodId is not { } paymentMethodId) return;
        if (await ResolveSavingsCategoryIdAsync(transactions, logger, profileId, ct) is not { } savingsCategoryId) return;

        try
        {
            await transactions.DeleteSavingsSourceTransactionsAsync(profileId, source.Name, paymentMethodId, savingsCategoryId, ct);
        }
        catch (Exception ex)
        {
            // Orphaned savings rows keep counting against the budget.
            logger.LogError(ex, "savings.delete_transactions_failed savings_source_id={SavingsSourceId} profile_id={ProfileId}", source.Id, profileId);
        }
    }

    private static async Task<int?> ResolveSavingsCategoryIdAsync(
        ITransactionRepository transactions, ILogger logger, Guid profileId, CancellationToken ct)
    {
        Dictionary<string, int> systemCategories;
        try { systemCategories = await transactions.ListSystemCategoriesAsync(ct); }
        catch (Exception ex)
        {
            logger.LogError(ex, "savings.list_system_categories_failed profile_id={ProfileId}", profileId);
            return null;
        }
        return systemCategories.TryGetValue(Carryover.SavingsSystemKey, out var id) ? id : null;
    }
}
