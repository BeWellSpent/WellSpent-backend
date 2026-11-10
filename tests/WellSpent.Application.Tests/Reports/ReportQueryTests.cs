using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Reports;
using WellSpent.Application.Reports.GetCategoryTrends;
using WellSpent.Application.Reports.GetPlanScorecard;
using Microsoft.Extensions.Logging.Abstractions;
using WellSpent.Application.FixedExpenses.GetFixedExpenseProjection;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Reports;

public sealed class ReportQueryTests
{
    private const int GroceriesId = 1, RentId = 2, Me = 1, Sam = 2;
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IExpenseAllocationRepository _allocations = Substitute.For<IExpenseAllocationRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetPeriodPlanRepository _plans = Substitute.For<IBudgetPeriodPlanRepository>();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _myCard = Guid.NewGuid();
    private readonly Guid _samCard = Guid.NewGuid();
    private readonly List<BudgetPeriod> _periods;

    public ReportQueryTests()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Six started monthly periods plus one in the future, which no report may include.
        _periods = Enumerable.Range(-5, 7).Select(offset =>
        {
            var start = new DateOnly(today.Year, today.Month, 1).AddMonths(offset);
            return new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = _profileId, StartDate = start, EndDate = start.AddMonths(1).AddDays(-1) };
        }).ToList();

        _profiles.GetByIdAsync(_profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = _profileId, UserId = _userId, Name = "B", Cycle = "monthly" });
        _profiles.ListPeriodsAsync(_profileId, Arg.Any<CancellationToken>()).Returns(_periods);
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int>());
        _profiles.ListPeopleAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = Me, BudgetProfileId = _profileId, Role = "admin" },
            new BudgetPerson { Id = Sam, BudgetProfileId = _profileId, Role = "collaborator" },
        ]);
        _allocations.ListAsync(_profileId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListSavingsSourcesAsync(_profileId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListIncomeSourcesAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { BudgetProfileId = _profileId, Name = "Mine", DefaultAmount = 3000m, BudgetPersonId = Me },
            new IncomeSource { BudgetProfileId = _profileId, Name = "Sam", DefaultAmount = 1000m, BudgetPersonId = Sam },
        ]);
        _profiles.ListIncomeEntriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ListPaymentMethodsAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new PaymentMethod { Id = _myCard, Name = "Mine", BudgetPersonId = Me },
            new PaymentMethod { Id = _samCard, Name = "Sam's", BudgetPersonId = Sam },
        ]);
        _fixedExpenses.ListAsync(_profileId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ListTransactionsAsync(Arg.Any<Guid>(), null, null, null, Arg.Any<CancellationToken>()).Returns([]);
        SetPlan("pro");
    }

    private void SetPlan(string plan) =>
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(new User { Id = _userId, Email = "u@example.com", Plan = plan });

    private BudgetPeriod Started(int indexFromEnd) => _periods[_periods.Count - 2 - indexFromEnd];

    private void Bills(BudgetPeriod period, params Transaction[] txs) =>
        _transactions.ListTransactionsAsync(period.Id, null, null, null, Arg.Any<CancellationToken>()).Returns(txs.ToList());

    private Transaction Spent(int category, decimal amount, Guid card) =>
        new() { Id = Guid.NewGuid(), CategoryId = category, Amount = amount, PlannedAmount = amount, TransactionTypeId = 2, PaymentMethodId = card };

    private GetFixedExpenseProjectionQueryHandler Projection => new(new BudgetAccessGuard(_profiles), _fixedExpenses, _profiles, _users, NullLogger<GetFixedExpenseProjectionQueryHandler>.Instance);
    private GetPlanScorecardQueryHandler Scorecard => new(new BudgetAccessGuard(_profiles), _profiles, _transactions, _allocations, _fixedExpenses, _users, _plans, NullLogger<GetPlanScorecardQueryHandler>.Instance);
    private GetCategoryTrendsQueryHandler Trends => new(new BudgetAccessGuard(_profiles), _profiles, _transactions, _allocations, _fixedExpenses, _users);

    [Fact]
    public async Task Projection_Pro_GroupsEndingPlansByMonth_AndSkipsOpenEndedBills()
    {
        var loanEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        _fixedExpenses.ListAsync(_profileId, Arg.Any<CancellationToken>(), null).Returns([
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Car loan", PlannedAmount = 420m, TotalPayments = 36, EndDate = loanEnd, PaymentMethodId = _myCard },
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Phone", PlannedAmount = 30m, EndDate = loanEnd.AddDays(1 - loanEnd.Day), PaymentMethodId = _myCard },
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Ended", PlannedAmount = 99m, EndDate = loanEnd.AddMonths(-4), PaymentMethodId = _myCard },
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Rent", PlannedAmount = 1850m, PaymentMethodId = _myCard },
        ]);

        var result = await Projection.Handle(new GetFixedExpenseProjectionQuery(_userId, _profileId), CancellationToken.None);

        Assert.False(result.Locked);
        var drop = Assert.Single(result.Projection!);
        Assert.Equal(new DateOnly(loanEnd.Year, loanEnd.Month, 1), drop.EndsOn);
        Assert.Equal(450m, drop.Amount.ToDecimal());
        Assert.Equal(["Car loan", "Phone"], drop.Names);
    }

    [Fact]
    public async Task Projection_Free_IsLockedWithNoData()
    {
        SetPlan("free");

        var result = await Projection.Handle(new GetFixedExpenseProjectionQuery(_userId, _profileId), CancellationToken.None);

        Assert.True(result.Locked);
        Assert.Null(result.Projection);
        await _fixedExpenses.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task Projection_FocusedView_ListsOnlyTheCallersPlans()
    {
        _profiles.GetPersonByUserIdAsync(_profileId, _userId, Arg.Any<CancellationToken>()).Returns(new BudgetPerson { Id = Me, BudgetProfileId = _profileId, Role = "admin" });

        await Projection.Handle(new GetFixedExpenseProjectionQuery(_userId, _profileId, FocusedView: true), CancellationToken.None);

        await _fixedExpenses.Received(1).ListAsync(_profileId, Arg.Any<CancellationToken>(), Me);
    }

    [Fact]
    public async Task Trends_MoversCompareTheCurrentPeriodWithTheAverageOfTheThreeBefore()
    {
        Bills(Started(3), Spent(GroceriesId, 600m, _myCard));
        Bills(Started(2), Spent(GroceriesId, 700m, _myCard));
        Bills(Started(1), Spent(GroceriesId, 800m, _myCard));
        Bills(Started(0), Spent(GroceriesId, 880m, _myCard), Spent(RentId, 5m, _samCard));

        var report = await Trends.Handle(new GetCategoryTrendsQuery(_userId, _profileId, 3, null), CancellationToken.None);

        Assert.Equal(3, report.Periods.Count); // only the requested 3, though 4 were loaded for the baseline
        Assert.Equal(885m, report.Periods[^1].Total!.Value.ToDecimal());
        Assert.All(report.Periods, p => Assert.NotNull(p.TotalPercentOfIncome)); // no income received, still a percent
        Assert.Equal(22.1, report.Periods[^1].TotalPercentOfIncome); // 885 of the 4000 planned
        var groceries = report.Movers.First();
        Assert.Equal(GroceriesId, groceries.CategoryId);
        Assert.Equal(700m, groceries.Average.ToDecimal());
        Assert.Equal(180m, groceries.Delta.ToDecimal());
    }

    [Fact]
    public async Task Trends_Free_LocksOlderPeriods_ButMoversStillWork()
    {
        SetPlan("free");
        Bills(Started(1), Spent(GroceriesId, 100m, _myCard));
        Bills(Started(0), Spent(GroceriesId, 300m, _myCard));

        var report = await Trends.Handle(new GetCategoryTrendsQuery(_userId, _profileId, 6, null), CancellationToken.None);

        Assert.Equal([true, true, false, false, false, false], report.Periods.Select(p => p.Locked));
        Assert.All(report.Periods.Where(p => p.Locked), p => Assert.Empty(p.Categories));
        Assert.NotEmpty(report.Movers);
    }

    [Fact]
    public async Task Trends_PersonFilter_UsesOnlyTheirSpending()
    {
        Bills(Started(0), Spent(GroceriesId, 50m, _myCard), Spent(GroceriesId, 70m, _samCard));

        var report = await Trends.Handle(new GetCategoryTrendsQuery(_userId, _profileId, 1, Sam), CancellationToken.None);

        var current = Assert.Single(report.Periods);
        Assert.Equal(70m, current.Total!.Value.ToDecimal());
        Assert.Equal(70m, Assert.Single(current.Categories).Amount.ToDecimal());
        Assert.Equal(7.0, current.TotalPercentOfIncome); // 70 of Sam's 1000 planned
    }

    private void Saved(BudgetPeriod period, int category, decimal amount, int? person = null) =>
        _savedPlans.Add(new BudgetPeriodPlan { BudgetPeriodId = period.Id, CategoryId = category, BudgetPersonId = person, PlannedAmount = amount });

    private readonly List<BudgetPeriodPlan> _savedPlans = [];

    [Fact]
    public async Task Scorecard_ClosedPeriodsUseTheirSavedPlan_CurrentUsesToday()
    {
        _plans.ListAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_savedPlans);
        _allocations.ListAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new ExpenseAllocation { BudgetProfileId = _profileId, CategoryId = GroceriesId, BudgetPersonId = Me, PlannedAmount = 500m },
        ]);
        Saved(Started(2), GroceriesId, 400m); Bills(Started(2), Spent(GroceriesId, 450m, _myCard)); // over its own plan
        Saved(Started(1), GroceriesId, 900m); Bills(Started(1), Spent(GroceriesId, 450m, _myCard)); // within
        Bills(Started(0), Spent(GroceriesId, 100m, _myCard));

        var report = await Scorecard.Handle(new GetPlanScorecardQuery(_userId, _profileId, 4, null), CancellationToken.None);

        var (noPlan, over, within, now) = (report.Periods[0], report.Periods[1], report.Periods[2], report.Periods[3]);
        Assert.False(noPlan.PlanRecorded);
        Assert.Null(noPlan.OnPlan);
        Assert.False(over.OnPlan);
        Assert.Equal(400m, over.PlannedSpending!.Value.ToDecimal());
        Assert.True(within.OnPlan);
        Assert.Equal((1, 1), (within.CategoriesWithin, within.CategoriesPlanned));
        Assert.True(now.InProgress);
        Assert.Null(now.OnPlan); // not scored yet
        Assert.Equal(500m, now.PlannedSpending!.Value.ToDecimal());
        var groceries = Assert.Single(report.Current);
        Assert.Equal((500m, 100m), (groceries.Planned.ToDecimal(), groceries.Actual.ToDecimal()));
        Assert.Equal((1, 2, 1), (report.OnPlanCount, report.ScoredCount, report.Streak));
    }

    [Fact]
    public async Task Scorecard_PersonFilter_UsesTheirShareOfThePlan()
    {
        _plans.ListAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_savedPlans);
        Saved(Started(1), GroceriesId, 600m); Saved(Started(1), GroceriesId, 100m, Sam);
        Bills(Started(1), Spent(GroceriesId, 150m, _samCard), Spent(GroceriesId, 50m, _myCard));

        var report = await Scorecard.Handle(new GetPlanScorecardQuery(_userId, _profileId, 2, Sam), CancellationToken.None);

        var closed = report.Periods[0];
        Assert.Equal((100m, 150m), (closed.PlannedSpending!.Value.ToDecimal(), closed.ActualSpending!.Value.ToDecimal()));
        Assert.False(closed.OnPlan);
    }

    [Fact]
    public async Task Scorecard_Free_LocksOlderPeriods()
    {
        SetPlan("free");
        _plans.ListAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_savedPlans);

        var report = await Scorecard.Handle(new GetPlanScorecardQuery(_userId, _profileId, 6, null), CancellationToken.None);

        Assert.Equal([true, true, false, false, false, false], report.Periods.Select(p => p.Locked));
        Assert.All(report.Periods.Where(p => p.Locked), p => Assert.Null(p.ActualSpending));
    }
}
