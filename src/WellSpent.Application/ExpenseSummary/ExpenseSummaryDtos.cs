using WellSpent.Application.Common;

namespace WellSpent.Application.ExpenseSummary;

public sealed record PersonExpenseSummaryDto(long BudgetPersonId, Money PlannedTotal, Money ActualTotal);

public sealed record PersonActualIncomeDto(long BudgetPersonId, Money ActualIncome);

/// <summary>One person's period for the Home screen. Spent figures include savings; Saved* is the savings part of each.</summary>
public sealed record PersonPeriodSummaryDto(
    long BudgetPersonId, Money SpentActual, Money SpentPlanned, Money SavedActual, Money SavedPlanned,
    Money IncomeActual, Money IncomePlanned);

/// <summary>
/// ActualTotal/IsOver/NotDuePlannedTotal/NextDueDate are only ever populated
/// on overview rows — plan rows are planned-side only, matching the Expense
/// Plan tab which never shows actuals.
/// </summary>
public sealed record CategoryExpenseSummaryDto(
    int CategoryId, Money PlannedTotal, Money? ActualTotal, bool IsOver,
    List<PersonExpenseSummaryDto> PersonBreakdowns, Money? NotDuePlannedTotal, DateOnly? NextDueDate,
    Money? AllocatedTotal = null, Money? FixedDueTotal = null);

public sealed record GetExpenseSummaryDto(
    Money IncomeFromSources, Money IncomeFromEntries,
    Money TotalCommitted, Money TotalPlanned, Money TotalActual, Money UncategorizedActual,
    Money TotalOverBudget, Money TotalUnplanned,
    Money RemainderPlan, Money RemainderActual, Money RemainderPlanned,
    List<CategoryExpenseSummaryDto> PlanCategories, List<CategoryExpenseSummaryDto> OverviewCategories,
    Money FixedActualTotal, Money VariableActualTotal, List<Guid> OverBudgetTransactionIds,
    Money TotalCommittedIncludingSavings, Money TotalPlannedIncludingSavings, Money TotalActualIncludingSavings,
    Money SavingsPlannedTotal, Money SavingsActualTotal,
    Money ActualIncomeTotal, Money RemainderActualIncome, List<PersonActualIncomeDto> ActualIncomeByPerson,
    Money RemainderPlanIncludingSavings,
    List<PersonPeriodSummaryDto> People, Money UnattributedSpent);
