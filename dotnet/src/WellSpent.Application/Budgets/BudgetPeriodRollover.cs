using Microsoft.Extensions.Logging;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets;

/// <summary>Shared period-rollover core (createNextPeriod) used by CreateBudgetProfile and CreateBudgetPeriod. Period-created notification stays deferred — Notification dispatch isn't wired anywhere in this port yet.</summary>
public static class BudgetPeriodRollover
{
    public static async Task<BudgetPeriod> CreateNextPeriodAsync(
        IBudgetProfileRepository profiles, ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses,
        TaxReserveRecalculator taxReserve, ILogger logger, BudgetProfile profile, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        BudgetPeriod? latest = null;
        try
        {
            latest = await profiles.GetLatestPeriodAsync(profile.Id, ct);
        }
        catch (NotFoundException)
        {
            // No previous period — first period for this profile.
        }

        DateOnly start, end;
        if (latest is null)
        {
            (start, end) = BudgetPeriodDates.ComputeFirst(profile.Cycle, today);
        }
        else if (latest.EndDate >= today)
        {
            // Idempotency: latest period is still active, nothing to create.
            return latest;
        }
        else
        {
            (start, end) = BudgetPeriodDates.ComputeNext(profile.Cycle, latest.EndDate);
        }

        var period = await profiles.CreatePeriodAsync(new BudgetPeriod
        {
            BudgetProfileId = profile.Id,
            StartDate = start,
            EndDate = end,
        }, ct);

        if (latest is not null)
        {
            // Two live periods at once: both clients pick a "current" period
            // and would disagree about which — mirrors Go's log-and-continue,
            // not a request failure.
            try
            {
                await profiles.ArchivePeriodAsync(latest.Id, ct);
            }
            catch
            {
                // Best-effort, matching Go.
            }
        }

        // Pre-fill recurring income sources as entries.
        var sources = await profiles.ListIncomeSourcesAsync(profile.Id, ct);
        foreach (var src in sources.Where(s => s.Recurring))
        {
            try
            {
                await profiles.CreateIncomeEntryAsync(new IncomeEntry
                {
                    BudgetPeriodId = period.Id,
                    IncomeSourceId = src.Id,
                    BudgetPersonId = src.BudgetPersonId,
                    Name = src.Name,
                    Amount = src.DefaultAmount,
                }, ct);
            }
            catch (Exception ex)
            {
                // The period opens with less income than the user expects,
                // which silently inflates every "remaining to allocate" figure.
                logger.LogError(ex, "period_rollover.income_prefill_failed income_source_id={IncomeSourceId} period_id={PeriodId}", src.Id, period.Id);
            }
        }

        // Recalculate per-person tax reserve entries.
        try
        {
            await taxReserve.RecalculateAsync(profile.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "period_rollover.tax_reserve_recalc_failed profile_id={ProfileId}", profile.Id);
        }

        // Spawn fixed-expense transactions for the new period — only for
        // expenses actually due this period's month, and at most once per
        // calendar month even if a weekly/bi-weekly cycle lands more than one
        // period inside that month. WEEK-unit expenses use a separate
        // per-date path since a single period can contain several due weeks.
        var activePaymentMethodIds = await ActivePaymentMethodIdsAsync(transactions, profile.Id, logger, ct);
        var monthStart = new DateOnly(start.Year, start.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        List<Domain.Entities.FixedExpense> activeFixedExpenses;
        try
        {
            activeFixedExpenses = await fixedExpenses.ListAsync(profile.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "period_rollover.list_fixed_expenses_failed profile_id={ProfileId}", profile.Id);
            activeFixedExpenses = [];
        }
        foreach (var fe in activeFixedExpenses)
        {
            try
            {
                if (fe.EndDate is { } endDate && start > endDate)
                {
                    // A finished payment plan keeps spawning bills the user no longer owes.
                    await fixedExpenses.DeactivateAsync(fe.Id, profile.Id, ct);
                    continue;
                }
                if (FixedExpenseScheduling.IsWeekUnit(fe))
                {
                    await FixedExpenseSpawning.SpawnWeeklyOccurrencesAsync(
                        transactions, fixedExpenses, fe, period.Id, start, end, activePaymentMethodIds, ct);
                    continue;
                }
                if (!FixedExpenseScheduling.IsDueInMonth(fe, monthStart)) continue;
                if (await fixedExpenses.HasTransactionInMonthAsync(fe.Id, monthStart, monthEnd, ct)) continue;

                await transactions.CreateTransactionAsync(new Transaction
                {
                    Name = fe.Name,
                    Amount = fe.PlannedAmount,
                    PlannedAmount = fe.PlannedAmount,
                    Date = FixedExpenseScheduling.DateInMonth(fe, monthStart),
                    BudgetPeriodId = period.Id,
                    CategoryId = fe.CategoryId,
                    PaymentMethodId = FixedExpenseSpawning.LivePaymentMethod(fe.PaymentMethodId, activePaymentMethodIds),
                    TransactionTypeId = 1,
                    FixedExpenseId = fe.Id,
                }, ct);
            }
            catch (Exception ex)
            {
                // A bill the user owes this period simply never appears.
                logger.LogError(ex, "period_rollover.fixed_expense_spawn_failed fixed_expense_id={FixedExpenseId} period_id={PeriodId}", fe.Id, period.Id);
            }
        }

        // Spawn savings-source transactions for the new period.
        var savingsSources = await profiles.ListSavingsSourcesAsync(profile.Id, ct);
        foreach (var src in savingsSources.Where(s => s.PaymentMethodId is not null && s.PaymentDays.Length > 0))
        {
            await SavingsTransactionSpawning.SpawnAsync(profiles, transactions, logger, profile.Id, src, ct);
        }

        if (latest is not null)
        {
            await PeriodCarryover.ApplyAsync(profiles, transactions, logger, profile, latest, period, ct);
        }

        // HOOK: period-created notification.

        return period;
    }

    /// <summary>Payment methods a spawned bill may be attributed to. Null on failure, which LivePaymentMethod reads as "don't second-guess the template" — losing attribution on every bill in a period because one lookup failed would be worse than the problem this guards against.</summary>
    private static async Task<HashSet<Guid>?> ActivePaymentMethodIdsAsync(
        ITransactionRepository transactions, Guid profileId, ILogger logger, CancellationToken ct)
    {
        try
        {
            var methods = await transactions.ListPaymentMethodsAsync(profileId, ct);
            return methods.Select(m => m.Id).ToHashSet();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "period_rollover.list_payment_methods_failed profile_id={ProfileId}", profileId);
            return null;
        }
    }
}
