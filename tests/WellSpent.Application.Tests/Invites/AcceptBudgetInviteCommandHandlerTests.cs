using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Invites.AcceptBudgetInvite;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Invites;

public sealed class AcceptBudgetInviteCommandHandlerTests
{
    private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private AcceptBudgetInviteCommandHandler CreateHandler() =>
        new(_invites, _profiles, _users, NullLogger<AcceptBudgetInviteCommandHandler>.Instance);

    private BudgetInvite PendingInvite(Guid token, Guid profileId, long? budgetPersonId = null) => new()
    {
        Id = Guid.NewGuid(), Token = token, BudgetProfileId = profileId, Email = "a@example.com",
        Role = "collaborator", Status = "pending", InvitedBy = Guid.NewGuid(),
        ExpiresAt = DateTime.UtcNow.AddDays(1), BudgetPersonId = budgetPersonId,
    };

    [Fact]
    public async Task ExpiredInvite_Propagates()
    {
        var token = Guid.NewGuid();
        var invite = PendingInvite(token, Guid.NewGuid());
        invite.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new AcceptBudgetInviteCommand(Guid.NewGuid(), token), CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyAMember_IsIdempotent_MarksAcceptedAndReturnsProfileId()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId);
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _users.GetByIdAsync(callerId, Arg.Any<CancellationToken>()).Returns(new User { Id = callerId, Email = "x@example.com" });
        _profiles.ExistsPersonForUserAsync(profileId, callerId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new AcceptBudgetInviteCommand(callerId, token), CancellationToken.None);

        Assert.Equal(profileId, result);
        await _profiles.DidNotReceive().AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().LinkPersonToUserAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invites.Received(1).UpdateStatusAsync(invite.Id, "accepted", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkedToExistingPlaceholder_LinksRatherThanCreates()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId, budgetPersonId: 7);
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _users.GetByIdAsync(callerId, Arg.Any<CancellationToken>()).Returns(new User { Id = callerId, Email = "x@example.com" });
        _profiles.ExistsPersonForUserAsync(profileId, callerId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(new AcceptBudgetInviteCommand(callerId, token), CancellationToken.None);

        Assert.Equal(profileId, result);
        await _profiles.Received(1).LinkPersonToUserAsync(7, callerId, "collaborator", Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoLinkedPerson_CreatesNewBudgetPerson()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId);
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _users.GetByIdAsync(callerId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = callerId, Email = "x@example.com", FirstName = "Ada", LastName = "Lovelace" });
        _profiles.ExistsPersonForUserAsync(profileId, callerId, Arg.Any<CancellationToken>()).Returns(false);

        await CreateHandler().Handle(new AcceptBudgetInviteCommand(callerId, token), CancellationToken.None);

        await _profiles.Received(1).AddPersonAsync(
            Arg.Is<BudgetPerson>(p => p.BudgetProfileId == profileId && p.UserId == callerId && p.UserName == "Ada Lovelace" && p.Role == "collaborator"),
            Arg.Any<CancellationToken>());
        await _invites.Received(1).UpdateStatusAsync(invite.Id, "accepted", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAcceptedFailure_IsSwallowed_StillReturnsProfileId()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId);
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _users.GetByIdAsync(callerId, Arg.Any<CancellationToken>()).Returns(new User { Id = callerId, Email = "x@example.com" });
        _profiles.ExistsPersonForUserAsync(profileId, callerId, Arg.Any<CancellationToken>()).Returns(true);
        _invites.UpdateStatusAsync(invite.Id, "accepted", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetInvite>(new InvalidOperationException("db hiccup")));

        var result = await CreateHandler().Handle(new AcceptBudgetInviteCommand(callerId, token), CancellationToken.None);

        Assert.Equal(profileId, result);
    }
}
