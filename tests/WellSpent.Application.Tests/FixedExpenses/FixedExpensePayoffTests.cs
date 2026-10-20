using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class FixedExpensePayoffTests
{
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();

    private static FixedExpense Plan(decimal plannedAmount = 20m, int? totalPayments = 6, bool isActive = true, DateOnly? endDate = null, DateOnly? paidOffDate = null) =>
        new()
        {
            Id = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(), Name = "Loan", PlannedAmount = plannedAmount,
            TotalPayments = totalPayments, IsActive = isActive, EndDate = endDate, PaidOffDate = paidOffDate,
        };

    // ── RemainingBalance ────────────────────────────────────────────────────

    [Fact]
    public void RemainingBalance_NoTotalPayments_IsZero()
    {
        var fe = Plan(totalPayments: null);
        Assert.Equal(0m, FixedExpensePayoff.RemainingBalance(fe, amountPaid: 0m));
    }

    [Fact]
    public void RemainingBalance_FullScheduleNoPayments_EqualsPlanTotal()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6);
        Assert.Equal(120m, FixedExpensePayoff.RemainingBalance(fe, amountPaid: 0m));
    }

    [Fact]
    public void RemainingBalance_ExtraPaymentOneMonth_ReducesFasterThanSchedule()
    {
        // 6 x $20 plan; $40 paid in month 2 (one regular + one extra) on top of month 1's $20.
        var fe = Plan(plannedAmount: 20m, totalPayments: 6);
        Assert.Equal(60m, FixedExpensePayoff.RemainingBalance(fe, amountPaid: 60m));
    }

    [Fact]
    public void RemainingBalance_ClampedToZero_NeverNegative()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6);
        Assert.Equal(0m, FixedExpensePayoff.RemainingBalance(fe, amountPaid: 500m));
    }

    // ── CheckAndApplyAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndApply_StillOwing_LeavesPlanActive()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };
        _fixedExpenses.SumPaidAmountAsync(fe.Id, Arg.Any<CancellationToken>()).Returns(20m);

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, new DateOnly(2026, 2, 1), NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().SetPaidOffAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        Assert.False(tx.FixedExpensePaidOffEarly);
    }

    [Fact]
    public async Task CheckAndApply_BalanceHitsZero_DeactivatesAndStampsPaidOffDate()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };
        _fixedExpenses.SumPaidAmountAsync(fe.Id, Arg.Any<CancellationToken>()).Returns(120m);
        var paidDate = new DateOnly(2026, 6, 1);

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, paidDate, NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.Received(1).SetPaidOffAsync(fe.Id, fe.BudgetProfileId, paidDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndApply_CompletedBeforeEndDate_FlagsTransactionPaidOffEarly()
    {
        var endDate = new DateOnly(2026, 12, 1);
        var fe = Plan(plannedAmount: 20m, totalPayments: 6, endDate: endDate);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };
        _fixedExpenses.SumPaidAmountAsync(fe.Id, Arg.Any<CancellationToken>()).Returns(120m);
        var paidDate = new DateOnly(2026, 3, 1); // well before endDate

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, paidDate, NullLogger.Instance, CancellationToken.None);

        Assert.True(tx.FixedExpensePaidOffEarly);
        await _transactions.Received(1).SetFixedExpensePaidOffEarlyAsync(tx.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndApply_CompletedOnOrAfterEndDate_DoesNotFlagEarly()
    {
        var endDate = new DateOnly(2026, 3, 1);
        var fe = Plan(plannedAmount: 20m, totalPayments: 6, endDate: endDate);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };
        _fixedExpenses.SumPaidAmountAsync(fe.Id, Arg.Any<CancellationToken>()).Returns(120m);

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, endDate, NullLogger.Instance, CancellationToken.None);

        Assert.False(tx.FixedExpensePaidOffEarly);
        await _transactions.DidNotReceive().SetFixedExpensePaidOffEarlyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndApply_NoEndDate_DoesNotFlagEarly()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6, endDate: null);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };
        _fixedExpenses.SumPaidAmountAsync(fe.Id, Arg.Any<CancellationToken>()).Returns(120m);

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, new DateOnly(2026, 3, 1), NullLogger.Instance, CancellationToken.None);

        Assert.False(tx.FixedExpensePaidOffEarly);
    }

    [Fact]
    public async Task CheckAndApply_AlreadyPaidOff_DoesNotReTrigger()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6, paidOffDate: new DateOnly(2026, 1, 1));
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, new DateOnly(2026, 3, 1), NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().SumPaidAmountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _fixedExpenses.DidNotReceive().SetPaidOffAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndApply_AlreadyInactive_DoesNotReTrigger()
    {
        var fe = Plan(plannedAmount: 20m, totalPayments: 6, isActive: false);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, new DateOnly(2026, 3, 1), NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().SetPaidOffAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndApply_NoTotalPayments_NeverRuns()
    {
        var fe = Plan(totalPayments: null);
        var tx = new Transaction { Id = Guid.NewGuid(), FixedExpenseId = fe.Id };

        await FixedExpensePayoff.CheckAndApplyAsync(_fixedExpenses, _transactions, tx, fe, new DateOnly(2026, 3, 1), NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().SumPaidAmountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
