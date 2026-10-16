using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Application.Invites.SendBudgetInvite;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Invites;

public sealed class SendBudgetInviteCommandHandlerTests
{
    private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private SendBudgetInviteCommandHandler CreateHandler() => new(
        _invites, _profiles, new BudgetAccessGuard(_profiles), _emailSender,
        Options.Create(new AuthOptions { FrontendUrl = "http://localhost:3000" }),
        NullLogger<SendBudgetInviteCommandHandler>.Instance);

    private (Guid AdminId, Guid ProfileId) SetUpAdminOwner()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Shared Budget" });
        return (adminId, profileId);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("unspecified")]
    [InlineData("")]
    public async Task DisallowedRole_Throws(string role)
    {
        var (adminId, profileId) = SetUpAdminOwner();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new SendBudgetInviteCommand(adminId, profileId, "x@example.com", role, null), CancellationToken.None));
    }

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(
            new SendBudgetInviteCommand(viewerId, profileId, "x@example.com", "collaborator", null), CancellationToken.None));
    }

    [Fact]
    public async Task Success_CreatesInvite_NormalizesEmail_SendsEmail_EmptyNamesInResponse()
    {
        var (adminId, profileId) = SetUpAdminOwner();
        _invites.CreateAsync(Arg.Any<BudgetInvite>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BudgetInvite>());

        var result = await CreateHandler().Handle(
            new SendBudgetInviteCommand(adminId, profileId, "  New@Example.com  ", "viewer", null), CancellationToken.None);

        Assert.Equal("new@example.com", result.Email);
        // Mirrors Go's toProtoInvite(inv, "", "") exactly for this RPC.
        Assert.Equal("", result.BudgetName);
        Assert.Equal("", result.InviterName);
        await _emailSender.Received(1).SendAsync("new@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkedPersonNotInBudget_Throws()
    {
        var (adminId, profileId) = SetUpAdminOwner();
        _profiles.GetPersonAsync(42, profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", "42")));

        await Assert.ThrowsAsync<NotFoundException>(() => CreateHandler().Handle(
            new SendBudgetInviteCommand(adminId, profileId, "x@example.com", "viewer", 42), CancellationToken.None));
    }

    [Fact]
    public async Task EmailSendFailure_DoesNotFailTheInvite()
    {
        var (adminId, profileId) = SetUpAdminOwner();
        _invites.CreateAsync(Arg.Any<BudgetInvite>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<BudgetInvite>());
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("resend down")));

        var result = await CreateHandler().Handle(
            new SendBudgetInviteCommand(adminId, profileId, "x@example.com", "viewer", null), CancellationToken.None);

        Assert.Equal("x@example.com", result.Email);
    }
}
