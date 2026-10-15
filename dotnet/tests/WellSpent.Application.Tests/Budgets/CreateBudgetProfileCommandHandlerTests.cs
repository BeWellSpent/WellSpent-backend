using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.CreateBudgetProfile;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class CreateBudgetProfileCommandHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private TaxReserveRecalculator TaxReserve => new(_profiles, _users, NullLogger<TaxReserveRecalculator>.Instance);

    public CreateBudgetProfileCommandHandlerTests()
    {
        _fixedExpenses.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ListPaymentMethodsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ListSavingsSourcesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private CreateBudgetProfileCommandHandler CreateHandler() =>
        new(_profiles, _users, _transactions, _fixedExpenses, Mapper, TaxReserve, NullLogger<CreateBudgetProfileCommandHandler>.Instance);

    private User SetUpOwner(Guid userId, string? countryCode = "US")
    {
        var owner = new User { Id = userId, Email = "owner@example.com", FirstName = "Ann", CountryCode = countryCode };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(owner);
        return owner;
    }

    [Fact]
    public async Task AlreadyOwnsABudget_Throws()
    {
        var userId = Guid.NewGuid();
        _profiles.ListByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new BudgetProfile { Id = Guid.NewGuid(), UserId = userId, Name = "Existing" }]);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateBudgetProfileCommand(userId, "New Budget", "monthly"), CancellationToken.None));
    }

    [Fact]
    public async Task DuplicateName_Throws()
    {
        var userId = Guid.NewGuid();
        _profiles.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ExistsByNameAndUserAsync("My Budget", userId, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<DuplicateException>(() => CreateHandler().Handle(
            new CreateBudgetProfileCommand(userId, "My Budget", "monthly"), CancellationToken.None));
    }

    [Fact]
    public async Task Success_PropagatesOwnerCountry_AddsOwnerAsAdminPerson_CreatesFirstPeriod()
    {
        var userId = Guid.NewGuid();
        var owner = SetUpOwner(userId, "AR");
        _profiles.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ExistsByNameAndUserAsync("My Budget", userId, Arg.Any<CancellationToken>()).Returns(false);

        BudgetProfile? created = null;
        _profiles.CreateAsync(Arg.Any<BudgetProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => { created = ci.Arg<BudgetProfile>(); created.Id = Guid.NewGuid(); return created; });
        _profiles.GetLatestPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        _profiles.CreatePeriodAsync(Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BudgetPeriod>());
        _profiles.ListIncomeSourcesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(new CreateBudgetProfileCommand(userId, "My Budget", "monthly"), CancellationToken.None);

        Assert.Equal("AR", result.Profile.CountryCode);
        Assert.NotNull(result.Period);
        await _profiles.Received(1).AddPersonAsync(
            Arg.Is<BudgetPerson>(p => p.Role == "admin" && p.UserId == userId && p.UserName == "Ann"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerAddFailure_IsNonFatal_StillReturnsProfile()
    {
        var userId = Guid.NewGuid();
        SetUpOwner(userId);
        _profiles.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ExistsByNameAndUserAsync(Arg.Any<string>(), userId, Arg.Any<CancellationToken>()).Returns(false);
        _profiles.CreateAsync(Arg.Any<BudgetProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var p = ci.Arg<BudgetProfile>(); p.Id = Guid.NewGuid(); return p; });
        _profiles.AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new InvalidOperationException("db down")));
        _profiles.GetLatestPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        _profiles.CreatePeriodAsync(Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BudgetPeriod>());

        var result = await CreateHandler().Handle(new CreateBudgetProfileCommand(userId, "My Budget", "monthly"), CancellationToken.None);

        Assert.Equal("My Budget", result.Profile.Name);
    }

    [Fact]
    public async Task FirstPeriodCreationFailure_IsNonFatal_ReturnsProfileWithNullPeriod()
    {
        var userId = Guid.NewGuid();
        SetUpOwner(userId);
        _profiles.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _profiles.ExistsByNameAndUserAsync(Arg.Any<string>(), userId, Arg.Any<CancellationToken>()).Returns(false);
        _profiles.CreateAsync(Arg.Any<BudgetProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var p = ci.Arg<BudgetProfile>(); p.Id = Guid.NewGuid(); return p; });
        _profiles.AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BudgetPerson>());
        _profiles.GetLatestPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new InvalidOperationException("db down")));

        var result = await CreateHandler().Handle(new CreateBudgetProfileCommand(userId, "My Budget", "monthly"), CancellationToken.None);

        Assert.Null(result.Period);
    }
}
