using Microsoft.Extensions.Logging;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>
/// Saves a closing period's plan — each category's planned total and each
/// person's share, by the same rule Plan shows — so the scorecard can judge it
/// against what was planned then. Best-effort: a failure is logged, never blocks cycling.
/// </summary>
public sealed class PeriodPlanSnapshotter(
    IBudgetProfileRepository profiles, ITransactionRepository transactions, IExpenseAllocationRepository allocations,
    IFixedExpenseRepository fixedExpenses, IBudgetPeriodPlanRepository plans, ILogger<PeriodPlanSnapshotter> logger)
{
    public async Task SnapshotAsync(BudgetPeriod period, CancellationToken ct)
    {
        try
        {
            if (await plans.ExistsAsync(period.Id, ct))
            {
                logger.LogInformation("period_plan_snapshot.already_exists period_id={PeriodId}", period.Id);
                return;
            }

            var loader = new ExpenseSummaryDataLoader(profiles, transactions, allocations, fixedExpenses);
            var inputs = await loader.LoadProfileAsync(period.BudgetProfileId, ct);
            var summary = new ExpenseSummaryCalculator(await loader.LoadPeriodAsync(period, inputs, ct)).Response();

            var rows = Rows(period.Id, summary.PlanCategories);
            await plans.AddRangeAsync(rows, ct);
            logger.LogInformation("period_plan_snapshot.saved period_id={PeriodId} profile_id={ProfileId} rows={Rows}", period.Id, period.BudgetProfileId, rows.Count);
        }
        catch (Exception ex)
        {
            // The scorecard shows this period as "no plan recorded" rather than misjudging it.
            logger.LogError(ex, "period_plan_snapshot.failed period_id={PeriodId} profile_id={ProfileId}", period.Id, period.BudgetProfileId);
        }
    }

    /// <summary>One row per planned category (person null) plus one per person with a share of it.</summary>
    public static List<BudgetPeriodPlan> Rows(Guid periodId, IEnumerable<CategoryExpenseSummaryDto> planCategories)
    {
        var rows = new List<BudgetPeriodPlan>();
        foreach (var category in planCategories)
        {
            var total = category.PlannedTotal.ToDecimal();
            if (total == 0 || category.CategoryId <= 0) continue;
            rows.Add(new BudgetPeriodPlan { BudgetPeriodId = periodId, CategoryId = category.CategoryId, PlannedAmount = total });
            rows.AddRange(category.PersonBreakdowns
                .Where(p => p.PlannedTotal.ToDecimal() != 0)
                .Select(p => new BudgetPeriodPlan
                {
                    BudgetPeriodId = periodId, CategoryId = category.CategoryId,
                    BudgetPersonId = (int)p.BudgetPersonId, PlannedAmount = p.PlannedTotal.ToDecimal(),
                }));
        }
        return rows;
    }
}
