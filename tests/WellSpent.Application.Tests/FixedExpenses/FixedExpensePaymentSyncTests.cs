using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

/// <summary>Covers the completion-detection addition to MarkPaidAsync (issue #55) — the existing template-sync behavior is covered via MarkTransactionAsPaidCommand in TransactionHandlerTests.</summary>
public sealed class FixedExpensePaymentSyncTests
{
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();

    private (Guid FeId, Guid TxId, Guid PeriodId) SetUp(decimal plannedAmount, int totalPayments, decimal amount, DateOnly paidDate, DateOnly? endDate = null)
    {
        var feId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        _transactions.MarkTransactionAsPaidAsync(txId, periodId, amount, paidDate, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, Amount = amount, IsPaid = true, FixedExpenseId = feId });
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = plannedAmount, TotalPayments = totalPayments, EndDate = endDate });

        return (feId, txId, periodId);
    }

    [Fact]
    public async Task MarkPaid_NormalPayment_DoesNotCompletePlan()
    {
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 6, amount: 20m, paidDate: new DateOnly(2026, 1, 1));
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(20m);

        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 20m, new DateOnly(2026, 1, 1), autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        Assert.False(tx.FixedExpensePaidOffEarly);
        await _fixedExpenses.DidNotReceive().SetPaidOffAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPaid_PaymentCompletesPlan_Deactivates()
    {
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 6, amount: 20m, paidDate: new DateOnly(2026, 6, 1));
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 20m, new DateOnly(2026, 6, 1), autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.Received(1).SetPaidOffAsync(feId, Arg.Any<Guid>(), new DateOnly(2026, 6, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPaid_CompletesBeforeEndDate_FlagsPaidOffEarly()
    {
        var endDate = new DateOnly(2026, 12, 1);
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 6, amount: 120m, paidDate: new DateOnly(2026, 3, 1), endDate: endDate);
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 120m, new DateOnly(2026, 3, 1), autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        Assert.True(tx.FixedExpensePaidOffEarly);
    }

    [Fact]
    public async Task MarkPaid_CompletesOnOrAfterEndDate_DoesNotFlagEarly()
    {
        var endDate = new DateOnly(2026, 3, 1);
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 6, amount: 120m, paidDate: endDate, endDate: endDate);
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 120m, endDate, autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        Assert.False(tx.FixedExpensePaidOffEarly);
    }

    [Fact]
    public async Task MarkPaid_AutoSyncFalse_StillRunsCompletionDetection()
    {
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 6, amount: 120m, paidDate: new DateOnly(2026, 6, 1));
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 120m, new DateOnly(2026, 6, 1), autoSyncTemplate: false, default, NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().UpdateFromPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _fixedExpenses.Received(1).SetPaidOffAsync(feId, Arg.Any<Guid>(), new DateOnly(2026, 6, 1), Arg.Any<CancellationToken>());
    }

    // ── EndDate gates the PlannedAmount rewrite, independent of autoSyncTemplate ──

    [Fact]
    public async Task MarkPaid_EndDateSet_AutoSyncOn_SkipsPlannedAmountRewrite_ButStillCompletesOffOriginalAmount()
    {
        // 2x$20 plan, EndDate set; $20 already paid, this overpayment ($45) must complete off the original $20, never a rewritten amount.
        var endDate = new DateOnly(2026, 12, 1);
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 2, amount: 45m, paidDate: new DateOnly(2026, 2, 1), endDate: endDate);
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(65m); // $20 (payment 1) + $45 (this one)

        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 45m, new DateOnly(2026, 2, 1), autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.DidNotReceive().UpdateFromPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        // RemainingBalance = 2*20 - 65 = -25, clamped to 0 -> completes.
        await _fixedExpenses.Received(1).SetPaidOffAsync(feId, Arg.Any<Guid>(), new DateOnly(2026, 2, 1), Arg.Any<CancellationToken>());
        Assert.True(tx.FixedExpensePaidOffEarly);
    }

    [Fact]
    public async Task MarkPaid_EndDateNull_AutoSyncOn_StillRewritesPlannedAmount()
    {
        // No EndDate -> indefinite recurring bill, still gets the normal auto-sync rewrite.
        var (feId, txId, periodId) = SetUp(plannedAmount: 20m, totalPayments: 0, amount: 25m, paidDate: new DateOnly(2026, 1, 1), endDate: null);
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(25m);

        await FixedExpensePaymentSync.MarkPaidAsync(
            _transactions, _fixedExpenses, txId, periodId, 25m, new DateOnly(2026, 1, 1), autoSyncTemplate: true, default, NullLogger.Instance, CancellationToken.None);

        await _fixedExpenses.Received(1).UpdateFromPaymentAsync(
            feId, 25m, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
