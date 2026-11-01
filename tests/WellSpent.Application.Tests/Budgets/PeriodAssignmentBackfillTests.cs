using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

/// <summary>Idempotent safety-net backfill (issue #99) — reassigns a transaction only when it's actually mismatched and never overridden.</summary>
public sealed class PeriodAssignmentBackfillTests
{
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();

    private Task ApplyAsync() => PeriodAssignmentBackfill.ApplyAsync(_transactions, _profiles, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task NoMismatches_ReassignsNothing()
    {
        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await ApplyAsync();

        await _transactions.DidNotReceive().ReassignPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NullBudgetPeriodId_GetsAssignedToTheCoveringPeriod()
    {
        var txId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 15);
        var correctPeriod = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 31) };

        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns([new MismatchedPeriodAssignmentRow(txId, date, null, profileId)]);
        _profiles.GetPeriodByDateAsync(profileId, date, Arg.Any<CancellationToken>()).Returns(correctPeriod);

        await ApplyAsync();

        await _transactions.Received(1).ReassignPeriodAsync(txId, correctPeriod.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DateOutsideAssignedPeriod_GetsReassignedToCorrectPeriod()
    {
        var txId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var staleDateOutsideItsPeriod = new DateOnly(2026, 4, 1);
        var correctPeriod = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, StartDate = new DateOnly(2026, 4, 1), EndDate = new DateOnly(2026, 4, 30) };
        var staleCurrentPeriodId = Guid.NewGuid();

        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns([new MismatchedPeriodAssignmentRow(txId, staleDateOutsideItsPeriod, staleCurrentPeriodId, profileId)]);
        _profiles.GetPeriodByDateAsync(profileId, staleDateOutsideItsPeriod, Arg.Any<CancellationToken>()).Returns(correctPeriod);

        await ApplyAsync();

        await _transactions.Received(1).ReassignPeriodAsync(txId, correctPeriod.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverriddenRow_IsNeverReturnedByTheRepoSoItIsNeverTouched()
    {
        // ListMismatchedPeriodAssignmentsAsync itself excludes PeriodOverridden rows (repo-level
        // filter) — simulating "looks mismatched but is overridden" means the repo returns nothing.
        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await ApplyAsync();

        await _profiles.DidNotReceive().GetPeriodByDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().ReassignPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoCoveringPeriodAnywhere_LogsUnresolvedAndDoesNotThrow()
    {
        var txId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var date = new DateOnly(2026, 5, 1);

        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns([new MismatchedPeriodAssignmentRow(txId, date, Guid.NewGuid(), profileId)]);
        _profiles.GetPeriodByDateAsync(profileId, date, Arg.Any<CancellationToken>()).Returns((BudgetPeriod?)null);

        await ApplyAsync();

        await _transactions.DidNotReceive().ReassignPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoProfileIdResolvable_LogsUnresolvedWithoutQueryingPeriods()
    {
        var txId = Guid.NewGuid();

        _transactions.ListMismatchedPeriodAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns([new MismatchedPeriodAssignmentRow(txId, new DateOnly(2026, 6, 1), null, null)]);

        await ApplyAsync();

        await _profiles.DidNotReceive().GetPeriodByDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().ReassignPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
