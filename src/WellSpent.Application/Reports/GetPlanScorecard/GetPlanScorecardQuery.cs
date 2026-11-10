using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Reports.GetPlanScorecard;

public sealed record GetPlanScorecardQuery(Guid UserId, Guid BudgetProfileId, int Periods, long? BudgetPersonId) : IRequest<PlanScorecardDto>;

/// <summary>
/// Each recent period's spending against the plan it had: the plan saved when it
/// closed (budget_period_plan), and today's plan for the current one. Actuals are
/// the Expense Overview's, computed live.
/// </summary>
public sealed class GetPlanScorecardQueryHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IExpenseAllocationRepository allocations, IFixedExpenseRepository fixedExpenses, IUserRepository users,
    IBudgetPeriodPlanRepository plans, ILogger<GetPlanScorecardQueryHandler> logger)
    : IRequestHandler<GetPlanScorecardQuery, PlanScorecardDto>
{
    public async Task<PlanScorecardDto> Handle(GetPlanScorecardQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var isPro = ReportRules.IsPro(await users.GetByIdAsync(request.UserId, ct));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var loader = new ExpenseSummaryDataLoader(profiles, transactions, allocations, fixedExpenses);
        var inputs = await loader.LoadProfileAsync(request.BudgetProfileId, ct);
        var periods = ReportRules.RecentStarted(await profiles.ListPeriodsAsync(request.BudgetProfileId, ct) ?? [], today, request.Periods);
        var saved = (await plans.ListAsync(periods.Select(p => p.Id).ToList(), ct) ?? [])
            .Where(r => r.BudgetPersonId == request.BudgetPersonId)
            .ToLookup(r => r.BudgetPeriodId);

        var dtos = new List<ScorecardPeriodDto>();
        List<ScorecardCategoryDto> current = [];
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var indexFromEnd = periods.Count - 1 - i;
            if (ReportRules.IsLocked(isPro, indexFromEnd))
            {
                dtos.Add(new ScorecardPeriodDto(period.Id, period.StartDate, period.EndDate, true, false, false, null, null, null, null, null, null, null, null));
                continue;
            }

            var summary = new ExpenseSummaryCalculator(await loader.LoadPeriodAsync(period, inputs, ct)).Response();
            var actual = Actual(summary, request.BudgetPersonId);
            var inProgress = indexFromEnd == 0;
            Dictionary<int, decimal>? planned = inProgress
                ? LivePlan(summary, request.BudgetPersonId)
                : saved[period.Id].Any() ? saved[period.Id].ToDictionary(r => r.CategoryId, r => r.PlannedAmount) : null;

            if (planned is null)
            {
                logger.LogInformation("plan_scorecard.no_plan_recorded period_id={PeriodId} profile_id={ProfileId}", period.Id, request.BudgetProfileId);
                dtos.Add(new ScorecardPeriodDto(period.Id, period.StartDate, period.EndDate, false, false, false,
                    null, Money.FromDecimal(actual.Where(a => a.Key != inputs.SavingsCategoryId).Sum(a => a.Value)), null, null, null, null, null, null));
                continue;
            }

            var score = ScorecardRules.Evaluate(planned, actual, inputs.SavingsCategoryId);
            dtos.Add(new ScorecardPeriodDto(period.Id, period.StartDate, period.EndDate, false, inProgress, true,
                Money.FromDecimal(score.PlannedSpending), Money.FromDecimal(score.ActualSpending),
                inProgress ? null : score.OnPlan, score.CategoriesWithin, score.CategoriesPlanned,
                Money.FromDecimal(score.SavingsPlanned), Money.FromDecimal(score.SavingsActual), inProgress ? null : score.SavingsMet));

            if (inProgress)
            {
                current = planned.Keys.Union(actual.Keys)
                    .Where(id => id != inputs.SavingsCategoryId)
                    .Select(id => new ScorecardCategoryDto(id, Money.FromDecimal(planned.GetValueOrDefault(id)), Money.FromDecimal(actual.GetValueOrDefault(id))))
                    .Where(c => c.Planned.ToDecimal() != 0 || c.Actual.ToDecimal() != 0)
                    .OrderByDescending(c => c.Planned.ToDecimal())
                    .ThenByDescending(c => c.Actual.ToDecimal())
                    .ToList();
            }
        }

        var scored = dtos.Where(d => d.OnPlan is not null).ToList();
        return new PlanScorecardDto(dtos, current, scored.Count(d => d.OnPlan == true), scored.Count, ScorecardRules.Streak(dtos.Select(d => d.OnPlan)));
    }

    /// <summary>Everyone: the Overview rows plus uncategorized as 0. One person: their own breakdowns.</summary>
    private static Dictionary<int, decimal> Actual(GetExpenseSummaryDto summary, long? personId)
    {
        if (personId is { } person)
        {
            return summary.OverviewCategories.ToDictionary(
                c => c.CategoryId,
                c => c.PersonBreakdowns.FirstOrDefault(b => b.BudgetPersonId == person)?.ActualTotal.ToDecimal() ?? 0m);
        }
        var all = summary.OverviewCategories.ToDictionary(c => c.CategoryId, c => c.ActualTotal?.ToDecimal() ?? 0m);
        var uncategorized = summary.UncategorizedActual.ToDecimal();
        if (uncategorized != 0) all[0] = uncategorized;
        return all;
    }

    /// <summary>The current period's plan, by the same rule the snapshot saves.</summary>
    private static Dictionary<int, decimal> LivePlan(GetExpenseSummaryDto summary, long? personId) =>
        PeriodPlanRows(summary, personId).ToDictionary(r => r.CategoryId, r => r.PlannedAmount);

    private static IEnumerable<BudgetPeriodPlan> PeriodPlanRows(GetExpenseSummaryDto summary, long? personId) =>
        Budgets.PeriodPlanSnapshotter.Rows(Guid.Empty, summary.PlanCategories)
            .Where(r => r.BudgetPersonId == personId);
}
