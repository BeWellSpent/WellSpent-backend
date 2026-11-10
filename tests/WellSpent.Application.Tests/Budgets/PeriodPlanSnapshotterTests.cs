using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class PeriodPlanSnapshotterTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IExpenseAllocationRepository _allocations = Substitute.For<IExpenseAllocationRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly IBudgetPeriodPlanRepository _plans = Substitute.For<IBudgetPeriodPlanRepository>();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly BudgetPeriod _period;

    public PeriodPlanSnapshotterTests()
    {
        _period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = _profileId, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) };
        _profiles.ListPeopleAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = 1, BudgetProfileId = _profileId, Role = "admin" },
            new BudgetPerson { Id = 2, BudgetProfileId = _profileId, Role = "collaborator" },
        ]);
        _allocations.ListAsync(_profileId, Arg.Any<CancellationToken>()).Returns([
            new ExpenseAllocation { BudgetProfileId = _profileId, CategoryId = 7, BudgetPersonId = 1, PlannedAmount = 300m },
            new ExpenseAllocation { BudgetProfileId = _profileId, CategoryId = 7, BudgetPersonId = 2, PlannedAmount = 100m },
        ]);
    }

    private PeriodPlanSnapshotter Snapshotter => new(_profiles, _transactions, _allocations, _fixedExpenses, _plans, NullLogger<PeriodPlanSnapshotter>.Instance);

    [Fact]
    public async Task Snapshot_SavesCategoryTotalAndEachPersonsShare()
    {
        IReadOnlyCollection<BudgetPeriodPlan>? saved = null;
        await _plans.AddRangeAsync(Arg.Do<IReadOnlyCollection<BudgetPeriodPlan>>(r => saved = r), Arg.Any<CancellationToken>());

        await Snapshotter.SnapshotAsync(_period, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.All(saved, r => Assert.Equal(_period.Id, r.BudgetPeriodId));
        Assert.Contains(saved, r => r.CategoryId == 7 && r.BudgetPersonId is null && r.PlannedAmount == 400m);
        Assert.Contains(saved, r => r.CategoryId == 7 && r.BudgetPersonId == 1 && r.PlannedAmount == 300m);
        Assert.Contains(saved, r => r.CategoryId == 7 && r.BudgetPersonId == 2 && r.PlannedAmount == 100m);
    }

    [Fact]
    public async Task Snapshot_AlreadyTaken_SavesNothing()
    {
        _plans.ExistsAsync(_period.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Snapshotter.SnapshotAsync(_period, CancellationToken.None);

        await _plans.DidNotReceive().AddRangeAsync(Arg.Any<IReadOnlyCollection<BudgetPeriodPlan>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Snapshot_Failure_IsSwallowed()
    {
        _plans.ExistsAsync(_period.Id, Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException("db down"));

        await Snapshotter.SnapshotAsync(_period, CancellationToken.None); // must not throw
    }

    [Fact]
    public void Rows_SkipUnplannedAndUncategorized()
    {
        var rows = PeriodPlanSnapshotter.Rows(_period.Id, [
            new CategoryExpenseSummaryDto(1, Money.FromDecimal(0m), null, false, [], null, null),
            new CategoryExpenseSummaryDto(0, Money.FromDecimal(50m), null, false, [], null, null),
            new CategoryExpenseSummaryDto(3, Money.FromDecimal(20m), null, false, [], null, null),
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(3, row.CategoryId);
    }
}
