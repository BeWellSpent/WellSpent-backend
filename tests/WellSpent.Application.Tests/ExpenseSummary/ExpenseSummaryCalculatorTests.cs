using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.ExpenseSummary;

public sealed class ExpenseSummaryCalculatorTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static ExpenseSummaryData Data(
        List<BudgetPerson>? people = null, List<ExpenseAllocation>? allocations = null,
        List<SavingsSource>? savingsSources = null, List<FixedExpense>? activeFixedExpenses = null,
        List<IncomeSource>? incomeSources = null, List<IncomeEntry>? incomeEntries = null,
        List<PaymentMethod>? paymentMethods = null, List<Transaction>? transactions = null,
        int? savingsCategoryId = null) =>
        new()
        {
            Period = new BudgetPeriod { Id = PeriodId, BudgetProfileId = ProfileId },
            People = people ?? [],
            Allocations = allocations ?? [],
            SavingsSources = savingsSources ?? [],
            ActiveFixedExpenses = activeFixedExpenses ?? [],
            IncomeSources = incomeSources ?? [],
            IncomeEntries = incomeEntries ?? [],
            PaymentMethods = paymentMethods ?? [],
            Transactions = transactions ?? [],
            SavingsCategoryId = savingsCategoryId,
            Now = Today,
        };

    private static Transaction Tx(
        int? categoryId = null, decimal amount = 0, decimal? plannedAmount = null, int transactionTypeId = 2,
        bool isPaid = false, Guid? paymentMethodId = null, Guid? fixedExpenseId = null, DateOnly? date = null, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(), CategoryId = categoryId, Amount = amount, PlannedAmount = plannedAmount ?? amount,
            TransactionTypeId = transactionTypeId, IsPaid = isPaid, PaymentMethodId = paymentMethodId,
            FixedExpenseId = fixedExpenseId, Date = date ?? Today,
        };

    [Fact]
    public void PlannedButUnspentCategory_VisibleInOverview()
    {
        const int catId = 10;
        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = catId, PlannedAmount = 50.00m }],
            incomeSources: [new IncomeSource { BudgetProfileId = ProfileId, Name = "Salary", DefaultAmount = 1000.00m }],
            incomeEntries: [new IncomeEntry { BudgetPeriodId = PeriodId, Amount = 1000.00m }]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var cat = Assert.Single(resp.OverviewCategories);
        Assert.Equal(catId, cat.CategoryId);
        Assert.Equal(50.00m, cat.PlannedTotal.ToDecimal());
        Assert.Equal(0m, cat.ActualTotal!.Value.ToDecimal());
        Assert.False(cat.IsOver);
        Assert.Equal(50.00m, resp.TotalPlanned.ToDecimal());
        Assert.Equal(0m, resp.TotalActual.ToDecimal());
        Assert.Equal(950.00m, resp.RemainderPlanned.ToDecimal());
        Assert.Equal(1000.00m, resp.RemainderActual.ToDecimal());
    }

    [Fact]
    public void BasicPlanAndOverviewParity()
    {
        const int catId = 1;
        var pmId = Guid.NewGuid();
        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = catId, PlannedAmount = 100.00m }],
            incomeSources: [new IncomeSource { BudgetProfileId = ProfileId, Name = "Salary", DefaultAmount = 500.00m }],
            incomeEntries: [new IncomeEntry { BudgetPeriodId = PeriodId, Amount = 500.00m }],
            transactions: [Tx(catId, 60.00m, 60.00m, paymentMethodId: pmId, isPaid: true)]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal(100.00m, resp.TotalCommitted.ToDecimal());
        Assert.Equal(100.00m, resp.TotalPlanned.ToDecimal());
        Assert.Equal(60.00m, resp.TotalActual.ToDecimal());
        Assert.Equal(400.00m, resp.RemainderPlan.ToDecimal());
        Assert.Equal(440.00m, resp.RemainderActual.ToDecimal());
        Assert.Equal(400.00m, resp.RemainderPlanned.ToDecimal());
        Assert.Equal(0m, resp.TotalOverBudget.ToDecimal());
        Assert.Equal(0m, resp.TotalUnplanned.ToDecimal());
        Assert.Equal(100.00m, Assert.Single(resp.PlanCategories).PlannedTotal.ToDecimal());
        Assert.Equal(100.00m, Assert.Single(resp.OverviewCategories).PlannedTotal.ToDecimal());
        Assert.Equal(60.00m, resp.OverviewCategories[0].ActualTotal!.Value.ToDecimal());
    }

    [Fact]
    public void FixedFallback_DueThisPeriod_CountsTowardCommitted()
    {
        const int catId = 2;
        // No allocation for this category — must fall back to the Fixed transaction's planned amount.
        var data = Data(transactions: [Tx(catId, 0m, 30.00m, transactionTypeId: 1, isPaid: false)]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal(30.00m, Assert.Single(resp.PlanCategories).PlannedTotal.ToDecimal());
        Assert.Equal(30.00m, resp.TotalCommitted.ToDecimal());
        Assert.Equal(0m, resp.OverviewCategories[0].ActualTotal!.Value.ToDecimal());
    }

    [Fact]
    public void NotDueFixedTemplate_InformationalOnly()
    {
        const int catId = 3;
        var data = Data(activeFixedExpenses: [new FixedExpense { BudgetProfileId = ProfileId, Name = "Prime", CategoryId = catId, PlannedAmount = 20.00m, IsActive = true, CreatedAt = Today.ToDateTime(TimeOnly.MinValue) }]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var row = Assert.Single(resp.PlanCategories);
        Assert.Equal(0m, row.PlannedTotal.ToDecimal());
        Assert.NotNull(row.NotDuePlannedTotal);
        Assert.Equal(20.00m, row.NotDuePlannedTotal!.Value.ToDecimal());
        Assert.NotNull(row.NextDueDate);
        Assert.Equal(0m, resp.TotalCommitted.ToDecimal());
        Assert.Equal(0m, resp.TotalPlanned.ToDecimal());
        Assert.Empty(resp.OverviewCategories);
    }

    [Fact]
    public void NotDueFixedTemplate_EarliestDueDateWins()
    {
        const int catId = 3;
        var created = Today.ToDateTime(TimeOnly.MinValue);
        var data = Data(activeFixedExpenses: [
            new FixedExpense { BudgetProfileId = ProfileId, Name = "A", CategoryId = catId, PlannedAmount = 20.00m, IsActive = true, DayOfMonth = 28, CreatedAt = created },
            new FixedExpense { BudgetProfileId = ProfileId, Name = "B", CategoryId = catId, PlannedAmount = 5.00m, IsActive = true, DayOfMonth = 2, CreatedAt = created },
        ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var row = Assert.Single(resp.PlanCategories);
        Assert.Equal(25.00m, row.NotDuePlannedTotal!.Value.ToDecimal());
        Assert.NotNull(row.NextDueDate);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 2), row.NextDueDate);
    }

    [Fact]
    public void PlanRowsSumToTotalCommitted()
    {
        const int allocCat = 1, dueCat = 2, notDueCat = 3;
        var methodId = Guid.NewGuid();
        var created = Today.ToDateTime(TimeOnly.MinValue);
        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = allocCat, PlannedAmount = 100.00m }],
            transactions: [Tx(dueCat, 0m, 30.00m, transactionTypeId: 1, paymentMethodId: methodId)],
            activeFixedExpenses: [new FixedExpense { BudgetProfileId = ProfileId, Name = "Upcoming", CategoryId = notDueCat, PlannedAmount = 20.00m, IsActive = true, CreatedAt = created }]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal(3, resp.PlanCategories.Count);
        var sum = resp.PlanCategories.Sum(r => r.PlannedTotal.ToDecimal());
        Assert.Equal(130.00m, sum);
        Assert.Equal(sum, resp.TotalCommitted.ToDecimal());
    }

    [Fact]
    public void SavingsCategory_UsesSavingsSourceSum()
    {
        const int savingsCatId = 4;
        var data = Data(
            savingsCategoryId: savingsCatId,
            savingsSources: [new SavingsSource { BudgetProfileId = ProfileId, Name = "A", Amount = 200.00m }, new SavingsSource { BudgetProfileId = ProfileId, Name = "B", Amount = 50.00m }],
            // Deliberately also give the Savings category an allocation — it must be ignored.
            allocations: [new ExpenseAllocation { CategoryId = savingsCatId, PlannedAmount = 999.00m }]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal(250.00m, Assert.Single(resp.PlanCategories).PlannedTotal.ToDecimal());
        Assert.Equal(250.00m, resp.TotalCommitted.ToDecimal());
    }

    [Fact]
    public void OverBudgetAndUnplanned()
    {
        const int plannedCat = 7;
        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = plannedCat, PlannedAmount = 50.00m }],
            transactions: [
                Tx(plannedCat, 80.00m, 80.00m, isPaid: true),
                Tx(null, 25.00m, 25.00m, isPaid: true),
            ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var cat = Assert.Single(resp.OverviewCategories);
        Assert.True(cat.IsOver);
        Assert.Equal(30.00m, resp.TotalOverBudget.ToDecimal());
        Assert.Equal(25.00m, resp.TotalUnplanned.ToDecimal());
        Assert.Equal(25.00m, resp.UncategorizedActual.ToDecimal());
        Assert.Equal(105.00m, resp.TotalActual.ToDecimal());
    }

    [Fact]
    public void PersonBreakdowns()
    {
        const int catId = 8;
        const int person1 = 1, person2 = 2;
        var data = Data(
            people: [new BudgetPerson { BudgetProfileId = ProfileId, Id = person1, Role = "admin" }, new BudgetPerson { BudgetProfileId = ProfileId, Id = person2, Role = "collaborator" }],
            allocations: [
                new ExpenseAllocation { CategoryId = catId, BudgetPersonId = person1, PlannedAmount = 30.00m },
                new ExpenseAllocation { CategoryId = catId, BudgetPersonId = person2, PlannedAmount = 70.00m },
            ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var breakdowns = Assert.Single(resp.PlanCategories).PersonBreakdowns;
        Assert.Equal(2, breakdowns.Count);
        Assert.Equal(30.00m, breakdowns.Single(b => b.BudgetPersonId == person1).PlannedTotal.ToDecimal());
        Assert.Equal(70.00m, breakdowns.Single(b => b.BudgetPersonId == person2).PlannedTotal.ToDecimal());
    }

    [Fact]
    public void ActualSplitByType_SumsToTotalActual()
    {
        const int catId = 7;
        var data = Data(transactions: [
            Tx(catId, 100.00m, transactionTypeId: 1, isPaid: true),
            // Unpaid Fixed: not spend yet, counts toward neither half.
            Tx(catId, 500.00m, transactionTypeId: 1, isPaid: false),
            Tx(catId, 30.00m, transactionTypeId: 2),
            Tx(null, 5.00m, transactionTypeId: 2),
        ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal(100.00m, resp.FixedActualTotal.ToDecimal());
        Assert.Equal(35.00m, resp.VariableActualTotal.ToDecimal());
        Assert.Equal(135.00m, resp.TotalActual.ToDecimal());
        Assert.Equal(resp.TotalActual.ToDecimal(), resp.FixedActualTotal.ToDecimal() + resp.VariableActualTotal.ToDecimal());
    }

    [Fact]
    public void OverBudgetIds_FlagFromCrossingPointOnward()
    {
        const int catId = 7;
        var under = Guid.NewGuid(); // running 40, plan 100 — under
        var crossing = Guid.NewGuid(); // running 110 — this one crosses
        var after = Guid.NewGuid(); // running 130 — already over

        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = catId, PlannedAmount = 100.00m }],
            // Deliberately out of chronological order: the walk has to sort.
            transactions: [
                Tx(catId, 20.00m, id: after, date: new DateOnly(2026, 6, 20)),
                Tx(catId, 40.00m, id: under, date: new DateOnly(2026, 6, 1)),
                Tx(catId, 70.00m, id: crossing, date: new DateOnly(2026, 6, 10)),
            ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.DoesNotContain(under, resp.OverBudgetTransactionIds);
        Assert.Contains(crossing, resp.OverBudgetTransactionIds);
        Assert.Contains(after, resp.OverBudgetTransactionIds);
        Assert.Equal(2, resp.OverBudgetTransactionIds.Count);
    }

    [Fact]
    public void OverBudgetIds_IgnoreFixedAndReceived()
    {
        const int catId = 7;
        var data = Data(
            allocations: [new ExpenseAllocation { CategoryId = catId, PlannedAmount = 10.00m }],
            transactions: [
                // Paid Fixed well past the plan — never a filter candidate, the filter is about variable spending.
                Tx(catId, 900.00m, transactionTypeId: 1, isPaid: true, date: new DateOnly(2026, 6, 1)),
                // Money in cannot push a category over.
                Tx(catId, -50.00m, date: new DateOnly(2026, 6, 2)),
            ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Empty(resp.OverBudgetTransactionIds);
    }

    [Fact]
    public void OverBudgetIds_UnplannedCategoryFlagsFromFirstSpend()
    {
        const int catId = 7;
        var first = Guid.NewGuid();
        var data = Data(transactions: [Tx(catId, 5.00m, id: first, date: new DateOnly(2026, 6, 1))]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        Assert.Equal([first], resp.OverBudgetTransactionIds);
    }

    [Fact]
    public void NotDueTemplateSurvivesADueSiblingInTheSameCategory()
    {
        const int catId = 3;
        var dueTemplateId = Guid.NewGuid();
        var upcomingTemplateId = Guid.NewGuid();
        var created = Today.ToDateTime(TimeOnly.MinValue);

        var data = Data(
            // Netflix: spawned into this period from dueTemplateId.
            transactions: [Tx(catId, 15.00m, 15.00m, transactionTypeId: 1, isPaid: true, fixedExpenseId: dueTemplateId)],
            activeFixedExpenses: [
                new FixedExpense { Id = dueTemplateId, BudgetProfileId = ProfileId, Name = "Netflix", CategoryId = catId, PlannedAmount = 15.00m, IsActive = true, DayOfMonth = 10, CreatedAt = created },
                new FixedExpense { Id = upcomingTemplateId, BudgetProfileId = ProfileId, Name = "Prime", CategoryId = catId, PlannedAmount = 139.00m, IsActive = true, DayOfMonth = 20, CreatedAt = created },
            ]);

        var resp = new ExpenseSummaryCalculator(data).Response();

        var row = Assert.Single(resp.PlanCategories);
        Assert.NotNull(row.NotDuePlannedTotal);
        // Only the upcoming template counts — the due one is already in PlannedTotal.
        Assert.Equal(139.00m, row.NotDuePlannedTotal!.Value.ToDecimal());
        // The due bill alone is the plan; an upcoming one is never a planned tier.
        Assert.Equal(15.00m, row.PlannedTotal.ToDecimal());
        Assert.NotNull(row.NextDueDate);
    }
}
