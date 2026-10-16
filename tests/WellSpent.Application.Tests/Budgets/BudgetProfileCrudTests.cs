using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.DeleteBudgetProfile;
using WellSpent.Application.Budgets.GetBudgetProfile;
using WellSpent.Application.Budgets.ListBudgetProfiles;
using WellSpent.Application.Budgets.SetBudgetAutoUpdatePlannedAmount;
using WellSpent.Application.Budgets.SetBudgetCarryoverEnabled;
using WellSpent.Application.Budgets.UpdateBudgetProfile;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class BudgetProfileCrudTests
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
    public async Task List_DelegatesToListByUserOrMember()
    {
        var userId = Guid.NewGuid();
        _profiles.ListByUserOrMemberAsync(userId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListBudgetProfilesQueryHandler(_profiles, Mapper).Handle(new ListBudgetProfilesQuery(userId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Get_NonMember_Throws()
    {
        var (_, profileId) = SetUpOwner();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new GetBudgetProfileQueryHandler(Access, Mapper).Handle(
            new GetBudgetProfileQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Update_NonAdmin_ThrowsForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdateBudgetProfileCommandHandler(Access, _profiles, Mapper).Handle(
            new UpdateBudgetProfileCommand(viewerId, profileId, "New Name", "weekly"), CancellationToken.None));
    }

    [Fact]
    public async Task Update_Owner_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.UpdateAsync(profileId, "New Name", "weekly", Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "New Name", Cycle = "weekly" });

        var result = await new UpdateBudgetProfileCommandHandler(Access, _profiles, Mapper).Handle(
            new UpdateBudgetProfileCommand(adminId, profileId, "New Name", "weekly"), CancellationToken.None);

        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task SetCarryoverEnabled_RequiresAdmin()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "collaborator" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new SetBudgetCarryoverEnabledCommandHandler(Access, _profiles, Mapper).Handle(
            new SetBudgetCarryoverEnabledCommand(viewerId, profileId, true), CancellationToken.None));
    }

    [Fact]
    public async Task SetAutoUpdatePlannedAmount_Owner_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.SetAutoUpdatePlannedAmountAsync(profileId, false, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x", AutoUpdatePlannedAmount = false });

        var result = await new SetBudgetAutoUpdatePlannedAmountCommandHandler(Access, _profiles, Mapper).Handle(
            new SetBudgetAutoUpdatePlannedAmountCommand(adminId, profileId, false), CancellationToken.None);

        Assert.False(result.AutoUpdatePlannedAmount);
    }

    [Fact]
    public async Task Delete_NonAdmin_ThrowsForbidden_NeverCallsDelete()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteBudgetProfileCommandHandler(Access, _profiles).Handle(
            new DeleteBudgetProfileCommand(viewerId, profileId), CancellationToken.None));

        await _profiles.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ReadsPaymentMethodIdsBeforeDelete_RemovesThemAfter()
    {
        var (adminId, profileId) = SetUpOwner();
        var pmIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _profiles.ListPaymentMethodIdsByBudgetProfileAsync(profileId, Arg.Any<CancellationToken>()).Returns(pmIds);

        var order = new List<string>();
        _profiles.When(p => p.DeleteAsync(profileId, Arg.Any<CancellationToken>())).Do(_ => order.Add("delete_profile"));
        _profiles.When(p => p.DeletePaymentMethodsByIdsAsync(pmIds, Arg.Any<CancellationToken>())).Do(_ => order.Add("delete_pms"));

        await new DeleteBudgetProfileCommandHandler(Access, _profiles).Handle(
            new DeleteBudgetProfileCommand(adminId, profileId), CancellationToken.None);

        Assert.Equal(["delete_profile", "delete_pms"], order);
    }

    [Fact]
    public async Task Delete_PaymentMethodListFailure_StillDeletesProfile_SkipsCleanup()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ListPaymentMethodIdsByBudgetProfileAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<Guid>>(new InvalidOperationException("db down")));

        await new DeleteBudgetProfileCommandHandler(Access, _profiles).Handle(
            new DeleteBudgetProfileCommand(adminId, profileId), CancellationToken.None);

        await _profiles.Received(1).DeleteAsync(profileId, Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().DeletePaymentMethodsByIdsAsync(Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>());
    }
}
