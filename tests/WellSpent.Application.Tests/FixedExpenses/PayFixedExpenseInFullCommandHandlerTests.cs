using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses.PayFixedExpenseInFull;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class PayFixedExpenseInFullCommandHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private PayFixedExpenseInFullCommandHandler Handler => new(
        Access, _fixedExpenses, _profiles, _transactions, NullLogger<PayFixedExpenseInFullCommandHandler>.Instance);

    private (Guid AdminId, Guid ProfileId) SetUpProfile()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B", Cycle = "monthly" });
        return (adminId, profileId);
    }

    [Fact]
    public async Task PayInFull_NoTotalPayments_ThrowsAppValidationException()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Rent", TotalPayments = null });

        await Assert.ThrowsAsync<AppValidationException>(() => Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 100m, new DateOnly(2026, 3, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task PayInFull_WrongProfile_ThrowsForbidden()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = Guid.NewGuid(), Name = "Rent", TotalPayments = 6 });

        await Assert.ThrowsAsync<ForbiddenException>(() => Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 100m, new DateOnly(2026, 3, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task PayInFull_ExistingUnpaidTransaction_MarksItPaid()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6 };
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = false });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 80m, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = true, Amount = 80m, FixedExpenseId = feId });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(20m); // doesn't complete the plan

        var result = await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 80m, new DateOnly(2026, 3, 1)), CancellationToken.None);

        await _transactions.Received(1).MarkTransactionAsPaidAsync(txId, periodId, 80m, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        Assert.Equal(txId, result.Transaction.Id);
    }

    [Fact]
    public async Task PayInFull_NoExistingTransaction_CreatesNewPaidTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6, CategoryId = 3 };
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>()).Returns((Transaction?)null);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 31) });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(80m);

        var result = await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 80m, new DateOnly(2026, 3, 15)), CancellationToken.None);

        await _transactions.Received(1).CreateTransactionAsync(
            Arg.Is<Transaction>(t => t.IsPaid && t.FixedExpenseId == feId && t.Amount == 80m && t.BudgetPeriodId == periodId), Arg.Any<CancellationToken>());
        Assert.True(result.Transaction.IsPaid);
    }

    [Fact]
    public async Task PayInFull_ExistingAlreadyPaidTransaction_CreatesAdditionalTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6 };
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = Guid.NewGuid(), BudgetPeriodId = periodId, IsPaid = true });
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 31) });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(80m);

        await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 100m, new DateOnly(2026, 3, 20)), CancellationToken.None);

        await _transactions.Received(1).CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().MarkTransactionAsPaidAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayInFull_NeverTouchesEndDate()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var endDate = new DateOnly(2027, 1, 1);
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6, EndDate = endDate };
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = false });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 120m, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = true, Amount = 120m, FixedExpenseId = feId });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        var result = await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 120m, new DateOnly(2026, 3, 1)), CancellationToken.None);

        Assert.Equal(endDate, result.Expense.EndDate);
        await _fixedExpenses.DidNotReceive().UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>());
        await _fixedExpenses.DidNotReceive().UpdateFromPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayInFull_NeverRewritesPlannedAmount()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6 };
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = false });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 999m, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = true, Amount = 999m, FixedExpenseId = feId });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(20m); // still owing — autoSyncTemplate would have rewritten PlannedAmount here

        await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 999m, new DateOnly(2026, 3, 1)), CancellationToken.None);

        await _fixedExpenses.DidNotReceive().UpdateFromPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayInFull_CompletingPlan_ResponseShowsZeroRemainingAndPaidOffDate()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var fe = new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6 };
        var updatedFe = new FixedExpense
        {
            Id = feId, BudgetProfileId = profileId, Name = "Loan", PlannedAmount = 20m, TotalPayments = 6,
            IsActive = false, PaidOffDate = new DateOnly(2026, 3, 1),
        };
        // 1st call: handler's own lookup. 2nd: inside MarkPaidAsync, before SetPaidOffAsync runs. 3rd: the handler's final re-fetch, after payoff was applied.
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>()).Returns(fe, fe, updatedFe);
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = false });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 80m, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, IsPaid = true, Amount = 80m, FixedExpenseId = feId });
        _fixedExpenses.SumPaidAmountAsync(feId, Arg.Any<CancellationToken>()).Returns(120m);

        var result = await Handler.Handle(
            new PayFixedExpenseInFullCommand(adminId, profileId, feId, 80m, new DateOnly(2026, 3, 1)), CancellationToken.None);

        await _fixedExpenses.Received(1).SetPaidOffAsync(feId, profileId, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>());
        Assert.Equal(0m, result.Expense.RemainingBalance.ToDecimal());
        Assert.False(result.Expense.IsActive);
        Assert.Equal(new DateOnly(2026, 3, 1), result.Expense.PaidOffDate);
    }
}
