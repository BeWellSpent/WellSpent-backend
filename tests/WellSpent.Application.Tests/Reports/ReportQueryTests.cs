using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Reports;
using WellSpent.Application.Reports.GetCategoryTrends;
using WellSpent.Application.Reports.GetCommitmentReport;
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

    private Transaction Fixed(int category, decimal planned, Guid card) =>
        new() { Id = Guid.NewGuid(), CategoryId = category, Amount = 0, PlannedAmount = planned, TransactionTypeId = 1, PaymentMethodId = card };

    private Transaction Spent(int category, decimal amount, Guid card) =>
        new() { Id = Guid.NewGuid(), CategoryId = category, Amount = amount, PlannedAmount = amount, TransactionTypeId = 2, PaymentMethodId = card };

    private GetCommitmentReportQueryHandler Commitments => new(new BudgetAccessGuard(_profiles), _profiles, _transactions, _allocations, _fixedExpenses, _users);
    private GetCategoryTrendsQueryHandler Trends => new(new BudgetAccessGuard(_profiles), _profiles, _transactions, _allocations, _fixedExpenses, _users);

    [Fact]
    public async Task Commitments_Pro_SixStartedPeriods_CurrentByCategory_AndProjection()
    {
        Bills(Started(0), Fixed(RentId, 1850m, _myCard), Fixed(GroceriesId, 100m, _samCard));
        var loanEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        _fixedExpenses.ListAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Car loan", PlannedAmount = 420m, TotalPayments = 36, EndDate = loanEnd, PaymentMethodId = _myCard },
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = _profileId, Name = "Rent", PlannedAmount = 1850m, PaymentMethodId = _myCard },
        ]);

        var report = await Commitments.Handle(new GetCommitmentReportQuery(_userId, _profileId, null), CancellationToken.None);

        Assert.Equal(6, report.Periods.Count);
        Assert.DoesNotContain(report.Periods, p => p.BudgetPeriodId == _periods[^1].Id); // the future period
        Assert.All(report.Periods, p => Assert.False(p.Locked));
        Assert.Equal(1950m, report.Periods[^1].Committed!.Value.ToDecimal());
        Assert.Equal([RentId, GroceriesId], report.Categories.Select(c => c.CategoryId));
        Assert.Equal(2, report.BillCount);
        Assert.Equal(1, report.PlanCount);
        Assert.Equal(48.8, report.CommittedPercentOfIncome); // 1950 of 4000
        var drop = Assert.Single(report.Projection!);
        Assert.Equal(new DateOnly(loanEnd.Year, loanEnd.Month, 1), drop.EndsOn);
        Assert.Equal(420m, drop.Amount.ToDecimal());
        Assert.False(report.ProjectionLocked);
    }

    [Fact]
    public async Task Commitments_Free_LocksOlderPeriodsAndTheProjection()
    {
        SetPlan("free");

        var report = await Commitments.Handle(new GetCommitmentReportQuery(_userId, _profileId, null), CancellationToken.None);

        Assert.Equal([true, true, false, false, false, false], report.Periods.Select(p => p.Locked));
        Assert.All(report.Periods.Where(p => p.Locked), p => Assert.Null(p.Committed));
        Assert.Null(report.Projection);
        Assert.True(report.ProjectionLocked);
    }

    [Fact]
    public async Task Commitments_PersonFilter_CountsOnlyTheirBillsAndIncome()
    {
        Bills(Started(0), Fixed(RentId, 1850m, _myCard), Fixed(GroceriesId, 100m, _samCard));

        var report = await Commitments.Handle(new GetCommitmentReportQuery(_userId, _profileId, Sam), CancellationToken.None);

        Assert.Equal(100m, report.Periods[^1].Committed!.Value.ToDecimal());
        Assert.Equal(1000m, report.IncomePlanned.ToDecimal());
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
    }
}
