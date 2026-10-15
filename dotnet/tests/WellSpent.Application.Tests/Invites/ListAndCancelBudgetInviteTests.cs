using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Invites.CancelBudgetInvite;
using WellSpent.Application.Invites.ListBudgetInvites;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Invites;

public sealed class ListBudgetInvitesQueryHandlerTests
{
    private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private ListBudgetInvitesQueryHandler CreateHandler() => new(_invites, new BudgetAccessGuard(_profiles));

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateHandler().Handle(new ListBudgetInvitesQuery(viewerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_ReturnsInvitesWithEmptyDenormalizedNames()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Budget" });
        _invites.ListByProfileAsync(profileId, Arg.Any<CancellationToken>()).Returns(
        [
            new BudgetInvite { Id = Guid.NewGuid(), BudgetProfileId = profileId, Email = "a@example.com", Role = "viewer", Status = "pending", InvitedBy = adminId },
        ]);

        var result = await CreateHandler().Handle(new ListBudgetInvitesQuery(adminId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("", result[0].BudgetName);
        Assert.Equal("", result[0].InviterName);
    }
}

public sealed class CancelBudgetInviteCommandHandlerTests
{
    private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private CancelBudgetInviteCommandHandler CreateHandler() => new(_invites, new BudgetAccessGuard(_profiles));

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _invites.GetByIdAsync(inviteId, Arg.Any<CancellationToken>())
            .Returns(new BudgetInvite { Id = inviteId, BudgetProfileId = profileId, Email = "a@example.com", Role = "viewer", Status = "pending", InvitedBy = Guid.NewGuid() });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateHandler().Handle(new CancelBudgetInviteCommand(viewerId, inviteId), CancellationToken.None));
    }

    [Fact]
    public async Task NotPending_Throws()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        _invites.GetByIdAsync(inviteId, Arg.Any<CancellationToken>())
            .Returns(new BudgetInvite { Id = inviteId, BudgetProfileId = profileId, Email = "a@example.com", Role = "viewer", Status = "accepted", InvitedBy = adminId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Budget" });

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new CancelBudgetInviteCommand(adminId, inviteId), CancellationToken.None));
    }

    [Fact]
    public async Task Pending_Admin_CancelsSuccessfully()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        _invites.GetByIdAsync(inviteId, Arg.Any<CancellationToken>())
            .Returns(new BudgetInvite { Id = inviteId, BudgetProfileId = profileId, Email = "a@example.com", Role = "viewer", Status = "pending", InvitedBy = adminId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Budget" });

        await CreateHandler().Handle(new CancelBudgetInviteCommand(adminId, inviteId), CancellationToken.None);

        await _invites.Received(1).UpdateStatusAsync(inviteId, "cancelled", Arg.Any<CancellationToken>());
    }
}
