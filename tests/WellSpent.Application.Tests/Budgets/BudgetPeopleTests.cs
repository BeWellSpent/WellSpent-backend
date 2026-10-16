using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.ListBudgetPeople;
using WellSpent.Application.Budgets.RemoveBudgetPerson;
using WellSpent.Application.Budgets.UpdateBudgetPerson;
using WellSpent.Application.Budgets.UpdateBudgetPersonRole;
using WellSpent.Application.Budgets.UpdateMyBudgetPreferences;
using WellSpent.Application.Budgets.UpdateMyFocusedViewPreference;
using WellSpent.Application.Budgets.UpdateMyManualMatchReviewPreference;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class BudgetPeopleTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
    private BudgetAccessGuard Access => new(_profiles);

    private (Guid AdminId, Guid ProfileId) SetUpOwner()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Shared Budget" });
        return (adminId, profileId);
    }

    [Fact]
    public async Task ListBudgetPeople_NonMember_Throws()
    {
        var (_, profileId) = SetUpOwner();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new ListBudgetPeopleQueryHandler(Access, _profiles, Mapper)
            .Handle(new ListBudgetPeopleQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateBudgetPerson_NonAdmin_ThrowsForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdateBudgetPersonCommandHandler(Access, _profiles, Mapper)
            .Handle(new UpdateBudgetPersonCommand(viewerId, profileId, 5, "#ff0000"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateBudgetPersonRole_Admin_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.UpdatePersonRoleAsync(5, profileId, "collaborator", Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 5, BudgetProfileId = profileId, Role = "collaborator" });

        var result = await new UpdateBudgetPersonRoleCommandHandler(Access, _profiles, Mapper)
            .Handle(new UpdateBudgetPersonRoleCommand(adminId, profileId, 5, "collaborator"), CancellationToken.None);

        Assert.Equal("collaborator", result.Role);
    }

    [Fact]
    public async Task UpdateMyBudgetPreferences_ViewerAllowed_WritesOwnRowOnly()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });
        _profiles.UpdatePersonPreferencesAsync(profileId, viewerId, "bar", null, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer", PlanChartType = "bar" });

        var result = await new UpdateMyBudgetPreferencesCommandHandler(Access, _profiles, Mapper)
            .Handle(new UpdateMyBudgetPreferencesCommand(viewerId, profileId, "bar", null), CancellationToken.None);

        Assert.Equal("bar", result.PlanChartType);
    }

    [Fact]
    public async Task UpdateMyManualMatchReviewPreference_NonMember_Throws()
    {
        var (_, profileId) = SetUpOwner();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdateMyManualMatchReviewPreferenceCommandHandler(Access, _profiles, Mapper)
            .Handle(new UpdateMyManualMatchReviewPreferenceCommand(strangerId, profileId, false), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMyFocusedViewPreference_Member_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.UpdatePersonFocusedViewPreferenceAsync(profileId, adminId, true, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = adminId, Role = "admin", FocusedViewEnabled = true });

        var result = await new UpdateMyFocusedViewPreferenceCommandHandler(Access, _profiles, Mapper)
            .Handle(new UpdateMyFocusedViewPreferenceCommand(adminId, profileId, true), CancellationToken.None);

        Assert.True(result.FocusedViewEnabled);
    }

    [Fact]
    public async Task RemoveBudgetPerson_ProtectsOwnerFromRemoval()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.GetPersonAsync(1, profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = adminId, Role = "admin" });

        await Assert.ThrowsAsync<AppValidationException>(() => new RemoveBudgetPersonCommandHandler(Access, _profiles)
            .Handle(new RemoveBudgetPersonCommand(adminId, profileId, 1, 0, null), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveBudgetPerson_NoReplacement_SoftRemovesOnly()
    {
        var (adminId, profileId) = SetUpOwner();
        var personId = 7;
        _profiles.GetPersonAsync(personId, profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = personId, BudgetProfileId = profileId, Role = "collaborator" });

        await new RemoveBudgetPersonCommandHandler(Access, _profiles)
            .Handle(new RemoveBudgetPersonCommand(adminId, profileId, personId, 0, null), CancellationToken.None);

        await _profiles.Received(1).SoftRemovePersonAsync(personId, profileId, Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().SoftRemovePersonAndReassignAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveBudgetPerson_WithReplacement_VerifiesReplacementBelongsToProfile_ThenReassigns()
    {
        var (adminId, profileId) = SetUpOwner();
        var personId = 7;
        var replacementId = 9;
        var replacementPmId = Guid.NewGuid();
        _profiles.GetPersonAsync(personId, profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = personId, BudgetProfileId = profileId, Role = "collaborator" });
        _profiles.GetPersonAsync(replacementId, profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = replacementId, BudgetProfileId = profileId, Role = "collaborator" });

        await new RemoveBudgetPersonCommandHandler(Access, _profiles)
            .Handle(new RemoveBudgetPersonCommand(adminId, profileId, personId, replacementId, replacementPmId), CancellationToken.None);

        await _profiles.Received(1).SoftRemovePersonAndReassignAsync(personId, profileId, replacementPmId, replacementId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveBudgetPerson_ReplacementNotInProfile_Throws()
    {
        var (adminId, profileId) = SetUpOwner();
        var personId = 7;
        var replacementId = 99;
        _profiles.GetPersonAsync(personId, profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = personId, BudgetProfileId = profileId, Role = "collaborator" });
        _profiles.GetPersonAsync(replacementId, profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", replacementId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new RemoveBudgetPersonCommandHandler(Access, _profiles)
            .Handle(new RemoveBudgetPersonCommand(adminId, profileId, personId, replacementId, null), CancellationToken.None));
    }
}
