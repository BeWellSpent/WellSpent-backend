using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary.GetExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.ExpenseSummary;

public sealed class GetExpenseSummaryQueryHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IExpenseAllocationRepository _allocations = Substitute.For<IExpenseAllocationRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    public GetExpenseSummaryQueryHandlerTests()
    {
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int>());
        _profiles.ListPeopleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _allocations.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListSavingsSourcesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _fixedExpenses.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListIncomeSourcesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListIncomeEntriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ListPaymentMethodsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ListTransactionsAsync(Arg.Any<Guid>(), null, null, null, Arg.Any<CancellationToken>()).Returns([]);
    }

    private GetExpenseSummaryQueryHandler CreateHandler() =>
        new(Access, _profiles, _transactions, _allocations, _fixedExpenses);

    [Fact]
    public async Task ForbiddenWhenNotMember()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "B" });
        _profiles.GetPersonByUserIdAsync(profileId, callerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", callerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => CreateHandler()
            .Handle(new GetExpenseSummaryQuery(callerId, periodId, false), CancellationToken.None));
    }

    [Fact]
    public async Task ExclusionRules_IsExcludedAndIncomeCategory()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const int catId = 5, incomeCatId = 6;
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = userId, Name = "B" });
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["income"] = incomeCatId });
        _transactions.ListTransactionsAsync(periodId, null, null, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, Amount = 40.00m, PlannedAmount = 40.00m, TransactionTypeId = 2, IsExcluded = true },
            new Transaction { Id = Guid.NewGuid(), CategoryId = incomeCatId, Amount = 2000.00m, PlannedAmount = 2000.00m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, Amount = 15.00m, PlannedAmount = 15.00m, TransactionTypeId = 2 },
        ]);

        var resp = await CreateHandler().Handle(new GetExpenseSummaryQuery(userId, periodId, false), CancellationToken.None);

        var cat = Assert.Single(resp.OverviewCategories);
        Assert.Equal(catId, cat.CategoryId);
        Assert.Equal(15.00m, cat.ActualTotal!.Value.ToDecimal());
        Assert.Equal(15.00m, resp.TotalActual.ToDecimal());
    }

    // A credit card payment arrives TWICE from Plaid: positive on the account
    // that paid and negative on the card that was paid, both filed under
    // Payment. Counting both nets the card's own purchases against its own
    // settlement — a real production bug (issue in spend_filter.go).
    [Fact]
    public async Task CardPaymentDoesNotCancelTheCardsOwnPurchases()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        const int personId = 1, shoppingCatId = 20, paymentCatId = 23;
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = userId, Name = "B" });
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new BudgetPerson { Id = personId, BudgetProfileId = profileId, Role = "admin" }]);
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["payment"] = paymentCatId });
        _transactions.ListPaymentMethodsAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new PaymentMethod { Id = cardId, Name = "Card", BudgetPersonId = personId }]);
        _transactions.ListTransactionsAsync(periodId, null, null, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), CategoryId = shoppingCatId, PaymentMethodId = cardId, Amount = 1477.86m, PlannedAmount = 1477.86m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = paymentCatId, PaymentMethodId = cardId, Amount = -988.35m, PlannedAmount = -988.35m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = paymentCatId, PaymentMethodId = cardId, Amount = -518.52m, PlannedAmount = -518.52m, TransactionTypeId = 2 },
        ]);

        var resp = await CreateHandler().Handle(new GetExpenseSummaryQuery(userId, periodId, false), CancellationToken.None);

        Assert.Equal(1477.86m, resp.TotalActual.ToDecimal());
        Assert.Equal(1477.86m, resp.VariableActualTotal.ToDecimal());
        var cat = Assert.Single(resp.OverviewCategories);
        Assert.Equal(shoppingCatId, cat.CategoryId);
        var breakdown = Assert.Single(cat.PersonBreakdowns);
        Assert.Equal(1477.86m, breakdown.ActualTotal.ToDecimal());
    }

    [Fact]
    public async Task FocusedView_ScopesToCallerAndUnattributed()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        const int catId = 1, myPersonId = 1, spousePersonId = 2;
        var myPmId = Guid.NewGuid();
        var spousePmId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = userId, Name = "B" });
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = myPersonId, BudgetProfileId = profileId, UserId = userId, Role = "admin" },
            new BudgetPerson { Id = spousePersonId, BudgetProfileId = profileId, UserId = spouseUserId, Role = "collaborator" },
        ]);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { BudgetProfileId = profileId, Name = "Mine", DefaultAmount = 500.00m, BudgetPersonId = myPersonId },
            new IncomeSource { BudgetProfileId = profileId, Name = "Spouse", DefaultAmount = 300.00m, BudgetPersonId = spousePersonId },
            new IncomeSource { BudgetProfileId = profileId, Name = "Unattributed", DefaultAmount = 50.00m },
        ]);
        _profiles.ListIncomeEntriesAsync(periodId, Arg.Any<CancellationToken>()).Returns([
            new IncomeEntry { BudgetPeriodId = periodId, Amount = 500.00m, BudgetPersonId = myPersonId },
            new IncomeEntry { BudgetPeriodId = periodId, Amount = 300.00m, BudgetPersonId = spousePersonId },
            new IncomeEntry { BudgetPeriodId = periodId, Amount = 50.00m },
        ]);
        _transactions.ListPaymentMethodsAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new PaymentMethod { Id = myPmId, Name = "Mine", BudgetPersonId = myPersonId },
            new PaymentMethod { Id = spousePmId, Name = "Spouse", BudgetPersonId = spousePersonId },
        ]);
        _transactions.ListTransactionsAsync(periodId, null, null, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, PaymentMethodId = myPmId, Amount = 60.00m, PlannedAmount = 60.00m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, PaymentMethodId = spousePmId, Amount = 40.00m, PlannedAmount = 40.00m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, Amount = 10.00m, PlannedAmount = 10.00m, TransactionTypeId = 2 },
        ]);

        var resp = await CreateHandler().Handle(new GetExpenseSummaryQuery(userId, periodId, true), CancellationToken.None);

        Assert.Equal(550.00m, resp.IncomeFromSources.ToDecimal());
        Assert.Equal(550.00m, resp.IncomeFromEntries.ToDecimal());
        Assert.Equal(70.00m, resp.TotalActual.ToDecimal());
        Assert.Equal(70.00m, Assert.Single(resp.OverviewCategories).ActualTotal!.Value.ToDecimal());
    }

    [Fact]
    public async Task FullView_IncludesEveryone()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        const int catId = 1, myPersonId = 1, spousePersonId = 2;
        var myPmId = Guid.NewGuid();
        var spousePmId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = userId, Name = "B" });
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = myPersonId, BudgetProfileId = profileId, UserId = userId, Role = "admin" },
            new BudgetPerson { Id = spousePersonId, BudgetProfileId = profileId, UserId = spouseUserId, Role = "collaborator" },
        ]);
        _transactions.ListPaymentMethodsAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new PaymentMethod { Id = myPmId, Name = "Mine", BudgetPersonId = myPersonId },
            new PaymentMethod { Id = spousePmId, Name = "Spouse", BudgetPersonId = spousePersonId },
        ]);
        _transactions.ListTransactionsAsync(periodId, null, null, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, PaymentMethodId = myPmId, Amount = 60.00m, PlannedAmount = 60.00m, TransactionTypeId = 2 },
            new Transaction { Id = Guid.NewGuid(), CategoryId = catId, PaymentMethodId = spousePmId, Amount = 40.00m, PlannedAmount = 40.00m, TransactionTypeId = 2 },
        ]);

        var resp = await CreateHandler().Handle(new GetExpenseSummaryQuery(userId, periodId, false), CancellationToken.None);

        Assert.Equal(100.00m, resp.TotalActual.ToDecimal());
    }
}
