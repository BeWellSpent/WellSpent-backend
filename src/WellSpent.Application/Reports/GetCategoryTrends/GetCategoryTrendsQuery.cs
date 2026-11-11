using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Reports.GetCategoryTrends;

public sealed record GetCategoryTrendsQuery(Guid UserId, Guid BudgetProfileId, int Periods, long? BudgetPersonId) : IRequest<CategoryTrendsReportDto>;

/// <summary>
/// Spending per category across recent periods — the Expense Overview's own
/// figures, period by period — and the biggest movers: the current period
/// against the average of up to 3 before it.
/// </summary>
public sealed class GetCategoryTrendsQueryHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IExpenseAllocationRepository allocations, IFixedExpenseRepository fixedExpenses, IUserRepository users)
    : IRequestHandler<GetCategoryTrendsQuery, CategoryTrendsReportDto>
{
    private const int MoverBaselinePeriods = 3;
    private const int MaxMovers = 5;

    private sealed record PeriodSpend(Dictionary<int, decimal> ByCategory, decimal Total, decimal PlannedIncome, FlowTotalsDto? Flows = null);

    public async Task<CategoryTrendsReportDto> Handle(GetCategoryTrendsQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var isPro = ReportRules.IsPro(await users.GetByIdAsync(request.UserId, ct));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requested = Math.Clamp(request.Periods, 1, ReportRules.MaxPeriods);

        var loader = new ExpenseSummaryDataLoader(profiles, transactions, allocations, fixedExpenses);
        var inputs = await loader.LoadProfileAsync(request.BudgetProfileId, ct);
        // Movers always need the current period and up to 3 before it, even when fewer are shown.
        var periods = ReportRules.RecentStarted(await profiles.ListPeriodsAsync(request.BudgetProfileId, ct) ?? [], today, Math.Max(requested, MoverBaselinePeriods + 1));

        var spend = new List<PeriodSpend?>();
        for (var i = 0; i < periods.Count; i++)
        {
            if (ReportRules.IsLocked(isPro, periods.Count - 1 - i))
            {
                spend.Add(null);
                continue;
            }
            var data = await loader.LoadPeriodAsync(periods[i], inputs, ct);
            var summary = new ExpenseSummaryCalculator(data).Response();
            spend.Add(Spend(summary, request.BudgetPersonId) with { Flows = FlowTotals.Compute(Mine(data.AllTransactions, inputs, request.BudgetPersonId)) });
        }

        var shown = periods.Count - Math.Min(requested, periods.Count);
        var periodDtos = periods.Select((p, i) => spend[i] is not { } s
                ? new TrendPeriodDto(p.Id, p.StartDate, p.EndDate, true, null, null, [])
                : new TrendPeriodDto(p.Id, p.StartDate, p.EndDate, false, Money.FromDecimal(s.Total), ReportRules.Percent(s.Total, s.PlannedIncome),
                    s.ByCategory.Where(c => c.Value != 0).OrderByDescending(c => c.Value)
                        .Select(c => new CategoryAmountDto(c.Key, Money.FromDecimal(c.Value), ReportRules.Percent(c.Value, s.PlannedIncome))).ToList(), s.Flows))
            .Skip(shown)
            .ToList();

        return new CategoryTrendsReportDto(periodDtos, Movers(spend));
    }

    /// <summary>One person's transactions by payment-method attribution, as the totals use; everyone's otherwise.</summary>
    private static IEnumerable<Transaction> Mine(List<Transaction> all, ExpenseSummaryProfileInputs inputs, long? personId)
    {
        if (personId is not { } person) return all;
        var methods = inputs.PaymentMethods.Where(pm => pm.BudgetPersonId == person).Select(pm => pm.Id).ToHashSet();
        return all.Where(t => t.PaymentMethodId is { } id && methods.Contains(id));
    }

    /// <summary>Everyone: the Overview rows plus uncategorized as category 0. One person: their own breakdowns.</summary>
    private static PeriodSpend Spend(GetExpenseSummaryDto summary, long? personId)
    {
        if (personId is { } person)
        {
            var mine = summary.People.FirstOrDefault(p => p.BudgetPersonId == person);
            var byCategory = summary.OverviewCategories.ToDictionary(
                c => c.CategoryId,
                c => c.PersonBreakdowns.FirstOrDefault(b => b.BudgetPersonId == person)?.ActualTotal.ToDecimal() ?? 0m);
            return new PeriodSpend(byCategory, mine?.SpentActual.ToDecimal() ?? 0m, mine?.IncomePlanned.ToDecimal() ?? 0m);
        }

        var all = summary.OverviewCategories.ToDictionary(c => c.CategoryId, c => c.ActualTotal?.ToDecimal() ?? 0m);
        var uncategorized = summary.UncategorizedActual.ToDecimal();
        if (uncategorized != 0) all[0] = uncategorized;
        return new PeriodSpend(all, summary.TotalActualIncludingSavings.ToDecimal(), summary.IncomeFromSources.ToDecimal());
    }

    private static List<CategoryMoverDto> Movers(List<PeriodSpend?> spend)
    {
        if (spend.Count < 2 || spend[^1] is not { } current) return [];
        var baseline = spend.Take(spend.Count - 1).TakeLast(MoverBaselinePeriods).OfType<PeriodSpend>().ToList();
        if (baseline.Count == 0) return [];

        return current.ByCategory.Keys.Concat(baseline.SelectMany(b => b.ByCategory.Keys)).Distinct()
            .Select(id =>
            {
                var now = current.ByCategory.GetValueOrDefault(id);
                var average = baseline.Average(b => b.ByCategory.GetValueOrDefault(id));
                return (Id: id, Now: now, Average: average, Delta: now - average);
            })
            .Where(m => Math.Abs(m.Delta) >= 1)
            .OrderByDescending(m => Math.Abs(m.Delta))
            .Take(MaxMovers)
            .Select(m => new CategoryMoverDto(m.Id, Money.FromDecimal(m.Now), Money.FromDecimal(Math.Round(m.Average, 2)),
                Money.FromDecimal(Math.Round(m.Delta, 2)), ReportRules.Percent(m.Delta, current.PlannedIncome)))
            .ToList();
    }
}
