using WellSpent.Application.Common;

namespace WellSpent.Application.ExpenseSummary;

public sealed record PersonExpenseSummaryDto(long BudgetPersonId, Money PlannedTotal, Money ActualTotal);

/// <summary>
/// ActualTotal/IsOver/NotDuePlannedTotal/NextDueDate are only ever populated
/// on overview rows — plan rows are planned-side only, matching the Expense
/// Plan tab which never shows actuals.
/// </summary>
public sealed record CategoryExpenseSummaryDto(
    int CategoryId, Money PlannedTotal, Money? ActualTotal, bool IsOver,
    List<PersonExpenseSummaryDto> PersonBreakdowns, Money? NotDuePlannedTotal, DateOnly? NextDueDate);

public sealed record GetExpenseSummaryDto(
    Money IncomeFromSources, Money IncomeFromEntries,
    Money TotalCommitted, Money TotalPlanned, Money TotalActual, Money UncategorizedActual,
    Money TotalOverBudget, Money TotalUnplanned,
    Money RemainderPlan, Money RemainderActual, Money RemainderPlanned,
    List<CategoryExpenseSummaryDto> PlanCategories, List<CategoryExpenseSummaryDto> OverviewCategories,
    Money FixedActualTotal, Money VariableActualTotal, List<Guid> OverBudgetTransactionIds);
