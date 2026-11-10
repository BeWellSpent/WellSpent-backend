using WellSpent.Application.Common;

namespace WellSpent.Application.Reports;

public sealed record CategoryAmountDto(int CategoryId, Money Amount, double? PercentOfIncome);

public sealed record TrendPeriodDto(
    Guid BudgetPeriodId, DateOnly StartDate, DateOnly EndDate, bool Locked,
    Money? Total, double? TotalPercentOfIncome, List<CategoryAmountDto> Categories);

public sealed record CategoryMoverDto(int CategoryId, Money Current, Money Average, Money Delta, double? DeltaPercentOfIncome);

public sealed record CategoryTrendsReportDto(List<TrendPeriodDto> Periods, List<CategoryMoverDto> Movers);
