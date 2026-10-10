using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets.AddSavingsSource;
using WellSpent.Application.Budgets.DeleteSavingsSource;
using WellSpent.Application.Budgets.ListSavingsSources;
using WellSpent.Application.Budgets.UpdateSavingsSource;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class SavingsSourceTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private AddSavingsSourceCommandHandler AddHandler => new(Access, _profiles, _transactions, NullLogger<AddSavingsSourceCommandHandler>.Instance);
    private UpdateSavingsSourceCommandHandler UpdateHandler => new(Access, _profiles, _transactions, NullLogger<UpdateSavingsSourceCommandHandler>.Instance);
    private DeleteSavingsSourceCommandHandler DeleteHandler => new(Access, _profiles, _transactions, NullLogger<DeleteSavingsSourceCommandHandler>.Instance);

    private (Guid AdminId, Guid ProfileId) SetUpOwner()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x" });
        // Savings-transaction spawn/cleanup is best-effort and swallows a missing period — not under test here.
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        return (adminId, profileId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Add_InvalidPaymentDaysCount_Throws(int count)
    {
        var (adminId, profileId) = SetUpOwner();
        var days = Enumerable.Range(1, count).ToArray();

        await Assert.ThrowsAsync<AppValidationException>(() => AddHandler
            .Handle(new AddSavingsSourceCommand(adminId, profileId, "Emergency fund", new Money(200, 0), null, days), CancellationToken.None));
    }

    [Theory]
    [InlineData(1, "monthly")]
    [InlineData(2, "bi_weekly")]
    [InlineData(4, "weekly")]
    public async Task Add_ValidPaymentDaysCount_InfersFrequency(int count, string expectedFrequency)
    {
        var (adminId, profileId) = SetUpOwner();
        var days = Enumerable.Range(1, count).ToArray();
        _profiles.AddSavingsSourceAsync(Arg.Any<SavingsSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SavingsSource>());

        var result = await AddHandler
            .Handle(new AddSavingsSourceCommand(adminId, profileId, "Emergency fund", new Money(200, 0), null, days), CancellationToken.None);

        Assert.Equal(expectedFrequency, result.Frequency);
    }

    [Fact]
    public async Task Add_WithPaymentMethod_InfersPersonFromIt()
    {
        var (adminId, profileId) = SetUpOwner();
        var pmId = Guid.NewGuid();
        _profiles.GetPaymentMethodBudgetPersonIdAsync(pmId, Arg.Any<CancellationToken>()).Returns(42);
        SavingsSource? captured = null;
        _profiles.AddSavingsSourceAsync(Arg.Any<SavingsSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.Arg<SavingsSource>(); return captured; });

        await AddHandler
            .Handle(new AddSavingsSourceCommand(adminId, profileId, "Emergency fund", new Money(200, 0), pmId, [1]), CancellationToken.None);

        Assert.Equal(42, captured!.BudgetPersonId);
    }

    [Fact]
    public async Task Add_UnknownPaymentMethod_Throws()
    {
        var (adminId, profileId) = SetUpOwner();
        var pmId = Guid.NewGuid();
        _profiles.GetPaymentMethodBudgetPersonIdAsync(pmId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new NotFoundException("payment_method", pmId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => AddHandler
            .Handle(new AddSavingsSourceCommand(adminId, profileId, "Emergency fund", new Money(200, 0), pmId, [1]), CancellationToken.None));
    }

    [Fact]
    public async Task Update_NoPaymentDays_PassesEmptyFrequency()
    {
        // Mirrors Go's own code (not its stale comment): omitting payment
        // days writes an empty-string frequency, it does not preserve the
        // existing one.
        var (adminId, profileId) = SetUpOwner();
        _profiles.GetSavingsSourceAsync(1, profileId, Arg.Any<CancellationToken>())
            .Returns(new SavingsSource { Id = 1, BudgetProfileId = profileId, Name = "x", Frequency = "weekly" });
        SavingsSource? captured = null;
        _profiles.UpdateSavingsSourceAsync(Arg.Any<SavingsSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.Arg<SavingsSource>(); return captured; });

        await UpdateHandler
            .Handle(new UpdateSavingsSourceCommand(adminId, 1, profileId, "x", new Money(200, 0), null, []), CancellationToken.None);

        Assert.Equal("", captured!.Frequency);
    }

    [Fact]
    public async Task Update_NotFound_Throws()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.GetSavingsSourceAsync(99, profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SavingsSource>(new NotFoundException("savings_source", "99")));

        await Assert.ThrowsAsync<NotFoundException>(() => UpdateHandler
            .Handle(new UpdateSavingsSourceCommand(adminId, 99, profileId, "x", new Money(200, 0), null, [1]), CancellationToken.None));
    }

    [Fact]
    public async Task List_Member_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ListSavingsSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new SavingsSource { Id = 1, BudgetProfileId = profileId, Name = "Fund", Amount = 100m, Frequency = "monthly", PaymentDays = [1] },
        ]);

        var result = await new ListSavingsSourcesQueryHandler(Access, _profiles)
            .Handle(new ListSavingsSourcesQuery(adminId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Fund", result[0].Name);
    }

    [Fact]
    public async Task Delete_ViewerForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => DeleteHandler
            .Handle(new DeleteSavingsSourceCommand(viewerId, 1, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Collaborator_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.GetSavingsSourceAsync(1, profileId, Arg.Any<CancellationToken>())
            .Returns(new SavingsSource { Id = 1, BudgetProfileId = profileId, Name = "x" });

        await DeleteHandler
            .Handle(new DeleteSavingsSourceCommand(adminId, 1, profileId), CancellationToken.None);

        await _profiles.Received(1).DeleteSavingsSourceAsync(1, profileId, Arg.Any<CancellationToken>());
    }
}
