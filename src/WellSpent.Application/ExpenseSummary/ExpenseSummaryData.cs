using WellSpent.Domain.Entities;

namespace WellSpent.Application.ExpenseSummary;

/// <summary>
/// Every input GetExpenseSummary's calculation needs, fetched up front so
/// ExpenseSummaryCalculator itself does no I/O. Mutable (not a record) on
/// purpose: ApplyFocusedView mutates these lists in place before the
/// calculator ever sees them, mirroring Go's pointer-receiver mutation
/// exactly rather than rebuilding a new immutable copy.
/// </summary>
public sealed class ExpenseSummaryData
{
    public required BudgetPeriod Period { get; init; }
    public required List<BudgetPerson> People { get; set; }
    public required List<ExpenseAllocation> Allocations { get; set; }
    public required List<SavingsSource> SavingsSources { get; set; }
    public required List<FixedExpense> ActiveFixedExpenses { get; set; }
    public required List<IncomeSource> IncomeSources { get; set; }
    public required List<IncomeEntry> IncomeEntries { get; set; }
    public required List<PaymentMethod> PaymentMethods { get; set; }

    /// <summary>Already exclusion-filtered (IsExcluded, or a non-spend category) — every downstream computation can assume every row here is real spending activity.</summary>
    public required List<Transaction> Transactions { get; set; }

    /// <summary>Null when the Savings system category hasn't been seeded yet in this environment.</summary>
    public int? SavingsCategoryId { get; init; }

    /// <summary>Anchors FixedExpenseScheduling.NextDueDate for the not-due informational field. Carried on the data rather than read inside the calculator so the calculator stays pure and a test can pin the clock.</summary>
    public required DateOnly Now { get; init; }
}
