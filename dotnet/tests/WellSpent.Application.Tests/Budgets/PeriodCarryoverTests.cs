using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

/// <summary>Ports Go's applyCarryover — the I/O shell around the pure Carryover compute.</summary>
public sealed class PeriodCarryoverTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly BudgetProfile _profile = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "x", CarryoverEnabled = true };
    private readonly BudgetPeriod _closing;
    private readonly BudgetPeriod _next;

    public PeriodCarryoverTests()
    {
        _closing = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = _profile.Id, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) };
        _next = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = _profile.Id, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) };
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["savings"] = 10, ["debt"] = 11, ["income"] = 1, ["payment"] = 2 });
    }

    private Task ApplyAsync() => PeriodCarryover.ApplyAsync(_profiles, _transactions, NullLogger.Instance, _profile, _closing, _next, CancellationToken.None);

    [Fact]
    public async Task CarryoverDisabled_DoesNothing()
    {
        var profile = new BudgetProfile { Id = _profile.Id, UserId = _profile.UserId, Name = "x", CarryoverEnabled = false };

        await PeriodCarryover.ApplyAsync(_profiles, _transactions, NullLogger.Instance, profile, _closing, _next, CancellationToken.None);

        await _transactions.DidNotReceive().CountCarriedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyCarried_IsIdempotent_DoesNotCreateAgain()
    {
        _transactions.CountCarriedAsync(_next.Id, _closing.Id, Arg.Any<CancellationToken>()).Returns(1);

        await ApplyAsync();

        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leftover_CreatesSavingsTransactionDatedAtNextPeriodStart()
    {
        _transactions.CountCarriedAsync(_next.Id, _closing.Id, Arg.Any<CancellationToken>()).Returns(0);
        _transactions.ListTransactionsAsync(_closing.Id, null, null, null, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Amount = 1500m, TransactionTypeId = 2 }]);
        _profiles.ListIncomeEntriesAsync(_closing.Id, Arg.Any<CancellationToken>()).Returns([new IncomeEntry { Amount = 2000m }]);

        await ApplyAsync();

        await _transactions.Received(1).CreateTransactionAsync(Arg.Is<Transaction>(t =>
            t.Amount == 500m && t.CategoryId == 10 && t.BudgetPeriodId == _next.Id
            && t.CarriedFromBudgetPeriodId == _closing.Id && t.Date == _next.StartDate
            && t.Name!.Contains("Left over from")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shortfall_CreatesDebtTransactionOnSpendingMethod()
    {
        var method = Guid.NewGuid();
        _transactions.CountCarriedAsync(_next.Id, _closing.Id, Arg.Any<CancellationToken>()).Returns(0);
        _transactions.ListTransactionsAsync(_closing.Id, null, null, null, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Amount = 2300m, TransactionTypeId = 2, PaymentMethodId = method }]);
        _profiles.ListIncomeEntriesAsync(_closing.Id, Arg.Any<CancellationToken>()).Returns([new IncomeEntry { Amount = 2000m }]);

        await ApplyAsync();

        await _transactions.Received(1).CreateTransactionAsync(Arg.Is<Transaction>(t =>
            t.Amount == 300m && t.CategoryId == 11 && t.PaymentMethodId == method
            && t.Name!.Contains("Carried balance from")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvenPeriod_CreatesNothing()
    {
        _transactions.CountCarriedAsync(_next.Id, _closing.Id, Arg.Any<CancellationToken>()).Returns(0);
        _transactions.ListTransactionsAsync(_closing.Id, null, null, null, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Amount = 2000m, TransactionTypeId = 2 }]);
        _profiles.ListIncomeEntriesAsync(_closing.Id, Arg.Any<CancellationToken>()).Returns([new IncomeEntry { Amount = 2000m }]);

        await ApplyAsync();

        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingSystemCategory_SkipsThatRowRatherThanCreatingUncategorized()
    {
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int> { ["income"] = 1 });
        _transactions.CountCarriedAsync(_next.Id, _closing.Id, Arg.Any<CancellationToken>()).Returns(0);
        _transactions.ListTransactionsAsync(_closing.Id, null, null, null, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Amount = 1500m, TransactionTypeId = 2 }]);
        _profiles.ListIncomeEntriesAsync(_closing.Id, Arg.Any<CancellationToken>()).Returns([new IncomeEntry { Amount = 2000m }]);

        await ApplyAsync();

        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }
}
