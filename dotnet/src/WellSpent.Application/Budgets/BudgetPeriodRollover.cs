using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets;

/// <summary>
/// Shared period-rollover core used by CreateBudgetProfile (first period) and
/// CreateBudgetPeriod (every period after). Ports only the date/archive
/// mechanics of Go's createNextPeriod (budget_profile_service.go) — the rest
/// of that function's orchestration is deliberately deferred to later B5
/// batches, since it depends on entities that don't exist in this backend
/// yet:
///   - income-source pre-fill + tax-reserve recalculation → B5 batch 2 (Income/Savings)
///   - fixed-expense spawn → B5 batch 5 (Installment plans + FixedExpenses)
///   - carryover → B5 batch 5/6
///   - period-created notification → wired once those land
/// Each hook point is marked below. Until then, CreateBudgetPeriod in this
/// backend only creates/archives period rows — correct and idempotent, just
/// not yet feature-complete. The live Go backend keeps serving all actual
/// period rollovers in the meantime.
/// </summary>
public static class BudgetPeriodRollover
{
    public static async Task<BudgetPeriod> CreateNextPeriodAsync(
        IBudgetProfileRepository profiles, BudgetProfile profile, CancellationToken ct)
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

        // HOOK: income pre-fill + tax reserve recalc (B5 batch 2).
        // HOOK: fixed-expense spawn (B5 batch 5).
        // HOOK: carryover (B5 batch 5/6).
        // HOOK: period-created notification.

        return period;
    }
}
