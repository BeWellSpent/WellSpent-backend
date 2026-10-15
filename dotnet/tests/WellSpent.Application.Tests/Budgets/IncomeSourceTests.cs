using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets.AddIncomeSource;
using WellSpent.Application.Budgets.DeleteIncomeSource;
using WellSpent.Application.Budgets.ListIncomeEntries;
using WellSpent.Application.Budgets.ListIncomeSources;
using WellSpent.Application.Budgets.UpdateIncomeEntry;
using WellSpent.Application.Budgets.UpdateIncomeSource;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class IncomeSourceTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private WellSpent.Application.Budgets.TaxReserveRecalculator TaxReserve =>
        new(_profiles, _users, NullLogger<WellSpent.Application.Budgets.TaxReserveRecalculator>.Instance);
    private WellSpent.Application.Common.BudgetAccessGuard Access => new(_profiles);

    private (Guid AdminId, Guid ProfileId) SetUpOwner(string plan = "pro")
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x" });
        _users.GetByIdAsync(adminId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = adminId, Email = "a@example.com", Plan = plan });
        return (adminId, profileId);
    }

    [Fact]
    public async Task Add_ViewerForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(viewerId, profileId, "Salary", "salary", new Money(5000, 0), true, null, "monthly", true), CancellationToken.None));
    }

    [Fact]
    public async Task Add_Collaborator_Allowed()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IncomeSource>());

        var result = await new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "Salary", "salary", new Money(5000, 0), true, null, "monthly", true), CancellationToken.None);

        Assert.Equal("Salary", result.Name);
        Assert.Equal(5000, result.DefaultAmount.Units);
    }

    [Fact]
    public async Task Add_FreeTier_TwoPerSamePerson_Throws()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "A", BudgetPersonId = 5 },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "B", BudgetPersonId = 5 },
        ]);

        await Assert.ThrowsAsync<AppValidationException>(() => new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "C", "salary", new Money(1000, 0), true, 5, "monthly", false), CancellationToken.None));
    }

    [Fact]
    public async Task Add_FreeTier_DifferentPerson_NotCounted()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "A", BudgetPersonId = 5 },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "B", BudgetPersonId = 5 },
        ]);
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IncomeSource>());

        var result = await new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "C", "salary", new Money(1000, 0), true, 9, "monthly", false), CancellationToken.None);

        Assert.Equal("C", result.Name);
    }

    [Fact]
    public async Task List_NonMember_Throws()
    {
        var (_, profileId) = SetUpOwner();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new ListIncomeSourcesQueryHandler(Access, _profiles)
            .Handle(new ListIncomeSourcesQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Update_RecalculatesTaxReserve()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IncomeSource>());

        await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true), CancellationToken.None);

        // Once for the access check, once inside TaxReserveRecalculator.
        await _profiles.Received(2).GetByIdAsync(profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ViewerForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new DeleteIncomeSourceCommand(viewerId, 1, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task ListEntries_ResolvesProfileFromPeriod()
    {
        var (adminId, profileId) = SetUpOwner();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.ListIncomeEntriesAsync(periodId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListIncomeEntriesQueryHandler(Access, _profiles)
            .Handle(new ListIncomeEntriesQuery(adminId, periodId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task UpdateEntry_ArchivedPeriod_ThrowsForbidden_EvenForNonMember()
    {
        // Mirrors Go's assertPeriodCollaborator: the archived check runs
        // before the role check.
        var periodId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdateIncomeEntryCommandHandler(Access, _profiles)
            .Handle(new UpdateIncomeEntryCommand(strangerId, 1, periodId, new Money(100, 0)), CancellationToken.None));

        await _profiles.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateEntry_Collaborator_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = false });
        _profiles.UpdateIncomeEntryAsync(1, periodId, 250m, Arg.Any<CancellationToken>())
            .Returns(new IncomeEntry { Id = 1, BudgetPeriodId = periodId, Amount = 250m });

        var result = await new UpdateIncomeEntryCommandHandler(Access, _profiles)
            .Handle(new UpdateIncomeEntryCommand(adminId, 1, periodId, new Money(250, 0)), CancellationToken.None);

        Assert.Equal(250, result.Amount.Units);
    }
}
