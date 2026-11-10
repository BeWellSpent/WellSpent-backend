using WellSpent.Application.Common;

namespace WellSpent.Application.Reports;

public sealed record CategoryAmountDto(int CategoryId, Money Amount, double? PercentOfIncome);

public sealed record CommitmentPeriodDto(Guid BudgetPeriodId, DateOnly StartDate, DateOnly EndDate, bool Locked, Money? Committed);

public sealed record CommitmentDropDto(DateOnly EndsOn, Money Amount, List<string> Names);

public sealed record CommitmentReportDto(
    List<CommitmentPeriodDto> Periods, List<CategoryAmountDto> Categories, Money IncomePlanned,
    double? CommittedPercentOfIncome, int BillCount, int PlanCount,
    List<CommitmentDropDto>? Projection, bool ProjectionLocked);

public sealed record TrendPeriodDto(
    Guid BudgetPeriodId, DateOnly StartDate, DateOnly EndDate, bool Locked,
    Money? Total, double? TotalPercentOfIncome, List<CategoryAmountDto> Categories);

public sealed record CategoryMoverDto(int CategoryId, Money Current, Money Average, Money Delta, double? DeltaPercentOfIncome);

public sealed record CategoryTrendsReportDto(List<TrendPeriodDto> Periods, List<CategoryMoverDto> Movers);
