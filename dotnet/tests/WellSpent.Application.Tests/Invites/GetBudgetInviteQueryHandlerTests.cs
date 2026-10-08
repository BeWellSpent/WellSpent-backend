using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Invites.GetBudgetInvite;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Invites;

public sealed class GetBudgetInviteQueryHandlerTests
{
    private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private GetBudgetInviteQueryHandler CreateHandler() =>
        new(_invites, _profiles, _users, NullLogger<GetBudgetInviteQueryHandler>.Instance);

    private BudgetInvite PendingInvite(Guid token, Guid profileId, Guid inviterId, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(), Token = token, BudgetProfileId = profileId, Email = "a@example.com",
        Role = "viewer", Status = "pending", InvitedBy = inviterId, ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task Cancelled_Throws()
    {
        var token = Guid.NewGuid();
        var invite = PendingInvite(token, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
        invite.Status = "cancelled";
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new GetBudgetInviteQuery(token), CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyAccepted_Throws()
    {
        var token = Guid.NewGuid();
        var invite = PendingInvite(token, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
        invite.Status = "accepted";
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new GetBudgetInviteQuery(token), CancellationToken.None));
    }

    [Fact]
    public async Task Expired_MarksExpiredAndThrows()
    {
        var token = Guid.NewGuid();
        var invite = PendingInvite(token, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1));
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new GetBudgetInviteQuery(token), CancellationToken.None));

        await _invites.Received(1).UpdateStatusAsync(invite.Id, "expired", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Valid_PopulatesBudgetNameAndInviterName()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId, inviterId, DateTime.UtcNow.AddDays(1));
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = inviterId, Name = "Family Budget" });
        _users.GetByIdAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = inviterId, Email = "inviter@example.com", FirstName = "Ada", LastName = "Lovelace" });

        var result = await CreateHandler().Handle(new GetBudgetInviteQuery(token), CancellationToken.None);

        Assert.Equal("Family Budget", result.BudgetName);
        Assert.Equal("Ada Lovelace", result.InviterName);
    }

    [Fact]
    public async Task Valid_InviterMissingLastName_FallsBackToEmail()
    {
        var token = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invite = PendingInvite(token, profileId, inviterId, DateTime.UtcNow.AddDays(1));
        _invites.GetByTokenAsync(token, Arg.Any<CancellationToken>()).Returns(invite);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = inviterId, Name = "Budget" });
        _users.GetByIdAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = inviterId, Email = "inviter@example.com", FirstName = "Ada", LastName = null });

        var result = await CreateHandler().Handle(new GetBudgetInviteQuery(token), CancellationToken.None);

        // Postgres NULL-propagation-through-concatenation semantics: a single
        // missing name part falls back to the email entirely here, unlike
        // UserDisplayRules.DisplayName's more forgiving behavior.
        Assert.Equal("inviter@example.com", result.InviterName);
    }
}
