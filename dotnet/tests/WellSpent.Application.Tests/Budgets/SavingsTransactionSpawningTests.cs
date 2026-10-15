using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

/// <summary>Ports Go's createSavingsTransactions/DeleteSavingsSourceTransactions pairing used by Add/Update/DeleteSavingsSource and period rollover.</summary>
public sealed class SavingsTransactionSpawningTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly Guid _profileId = Guid.NewGuid();

    private void SetUpPeriod(DateOnly start) =>
        _profiles.GetLatestPeriodAsync(_profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = _profileId, StartDate = start, EndDate = start.AddMonths(1) });

    private void SetUpSavingsCategory(int id = 7) =>
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int> { ["savings"] = id });

    [Fact]
    public async Task SpawnAsync_NoActivePeriod_CreatesNothing()
    {
        _profiles.GetLatestPeriodAsync(_profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", Amount = 300m, PaymentDays = [1] };

        await SavingsTransactionSpawning.SpawnAsync(_profiles, _transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpawnAsync_MissingSavingsCategory_CreatesNothing()
    {
        SetUpPeriod(new DateOnly(2026, 2, 1));
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int>());
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", Amount = 300m, PaymentDays = [1] };

        await SavingsTransactionSpawning.SpawnAsync(_profiles, _transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpawnAsync_SingleDay_CreatesOneFixedSavingsTransactionForFullAmount()
    {
        SetUpPeriod(new DateOnly(2026, 2, 1));
        SetUpSavingsCategory(7);
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", Amount = 300m, Frequency = "monthly", PaymentDays = [15] };

        await SavingsTransactionSpawning.SpawnAsync(_profiles, _transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.Received(1).CreateTransactionAsync(Arg.Is<Transaction>(t =>
            t.Amount == 300m && t.PlannedAmount == 300m && t.CategoryId == 7 && t.TransactionTypeId == 1
            && t.TransactionFrequencyId == 4 && t.Date == new DateOnly(2026, 2, 15)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpawnAsync_MultipleDays_SplitsAmountEvenly()
    {
        SetUpPeriod(new DateOnly(2026, 2, 1));
        SetUpSavingsCategory(7);
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", Amount = 300m, Frequency = "bi_weekly", PaymentDays = [1, 15] };

        await SavingsTransactionSpawning.SpawnAsync(_profiles, _transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.Received(2).CreateTransactionAsync(Arg.Is<Transaction>(t => t.Amount == 150m && t.TransactionFrequencyId == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpawnAsync_DayBeyondMonthEnd_ClampsToLastDay()
    {
        SetUpPeriod(new DateOnly(2026, 2, 1)); // February 2026 has 28 days
        SetUpSavingsCategory(7);
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", Amount = 100m, PaymentDays = [31] };

        await SavingsTransactionSpawning.SpawnAsync(_profiles, _transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.Received(1).CreateTransactionAsync(Arg.Is<Transaction>(t => t.Date == new DateOnly(2026, 2, 28)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteExistingTransactionsAsync_NoPaymentMethod_DoesNothing()
    {
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund" };

        await SavingsTransactionSpawning.DeleteExistingTransactionsAsync(_transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.DidNotReceive().ListSystemCategoriesAsync(Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().DeleteSavingsSourceTransactionsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteExistingTransactionsAsync_WithPaymentMethod_DeletesByNameMethodAndCategory()
    {
        var methodId = Guid.NewGuid();
        SetUpSavingsCategory(7);
        var source = new SavingsSource { Id = 1, BudgetProfileId = _profileId, Name = "Fund", PaymentMethodId = methodId };

        await SavingsTransactionSpawning.DeleteExistingTransactionsAsync(_transactions, NullLogger.Instance, _profileId, source, CancellationToken.None);

        await _transactions.Received(1).DeleteSavingsSourceTransactionsAsync(_profileId, "Fund", methodId, 7, Arg.Any<CancellationToken>());
    }
}
