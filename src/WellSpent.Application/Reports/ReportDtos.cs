using WellSpent.Application.Common;

namespace WellSpent.Application.Reports;

public sealed record CategoryAmountDto(int CategoryId, Money Amount, double? PercentOfIncome);

public sealed record TrendPeriodDto(
    Guid BudgetPeriodId, DateOnly StartDate, DateOnly EndDate, bool Locked,
    Money? Total, double? TotalPercentOfIncome, List<CategoryAmountDto> Categories);

public sealed record CategoryMoverDto(int CategoryId, Money Current, Money Average, Money Delta, double? DeltaPercentOfIncome);

public sealed record CategoryTrendsReportDto(List<TrendPeriodDto> Periods, List<CategoryMoverDto> Movers);

/// <summary>One period of the plan scorecard. Amounts are absent when locked or when no plan was recorded; the current period is InProgress and not scored.</summary>
public sealed record ScorecardPeriodDto(
    Guid BudgetPeriodId, DateOnly StartDate, DateOnly EndDate, bool Locked, bool InProgress, bool PlanRecorded,
    Money? PlannedSpending, Money? ActualSpending, bool? OnPlan, int? CategoriesWithin, int? CategoriesPlanned,
    Money? SavingsPlanned, Money? SavingsActual, bool? SavingsMet);

public sealed record ScorecardCategoryDto(int CategoryId, Money Planned, Money Actual);

public sealed record PlanScorecardDto(List<ScorecardPeriodDto> Periods, List<ScorecardCategoryDto> Current, int OnPlanCount, int ScoredCount, int Streak);
