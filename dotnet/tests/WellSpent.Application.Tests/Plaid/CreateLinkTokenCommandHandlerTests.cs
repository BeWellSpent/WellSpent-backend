using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.CreateLinkToken;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_CreateLinkToken_* suite.</summary>
public sealed class CreateLinkTokenCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private CreateLinkTokenCommandHandler CreateHandler() =>
        new(new PlaidAccessGuard(_users), new BudgetAccessGuard(_profiles), _items, _plaid, _crypto,
            Options.Create(new AuthOptions { EncryptionKey = "test-key" }));

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };

    [Fact]
    public async Task Success_ReturnsLinkToken()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _plaid.CreateLinkTokenAsync(user.Id.ToString(), "", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidLinkToken("link-token", "2099-01-01T00:00:00Z"));

        var result = await CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, profileId, null, ""), CancellationToken.None);

        Assert.Equal("link-token", result.LinkToken);
    }

    [Fact]
    public async Task ForwardsRedirectUri()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _plaid.CreateLinkTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlaidLinkToken("link-token", "2099-01-01T00:00:00Z"));

        await CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, profileId, null, "https://bewellspent.com/plaid-oauth-redirect"), CancellationToken.None);

        await _plaid.Received(1).CreateLinkTokenAsync(user.Id.ToString(), "", "https://bewellspent.com/plaid-oauth-redirect", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OmitsRedirectUriWhenNotProvided()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _plaid.CreateLinkTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlaidLinkToken("link-token", "2099-01-01T00:00:00Z"));

        await CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, profileId, null, ""), CancellationToken.None);

        await _plaid.Received(1).CreateLinkTokenAsync(user.Id.ToString(), "", "", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonUsUser_ThrowsForbiddenException()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "AR", Plan = "lifetime" };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, Guid.NewGuid(), null, ""), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_ThrowsAppValidationException()
    {
        var user = UsUser("free");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, Guid.NewGuid(), null, ""), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMode_PassesDecryptedAccessToken()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _items.GetByIdAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connectionId, UserId = user.Id, AccessToken = "encrypted", ItemId = "item-1" });
        _crypto.Decrypt("encrypted", "test-key").Returns("real-access-token");
        _plaid.CreateLinkTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlaidLinkToken("update-link-token", "2099-01-01T00:00:00Z"));

        var result = await CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, profileId, connectionId, ""), CancellationToken.None);

        Assert.Equal("update-link-token", result.LinkToken);
        await _plaid.Received(1).CreateLinkTokenAsync(user.Id.ToString(), "real-access-token", "", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMode_WrongUser_ThrowsForbiddenException()
    {
        var user = UsUser();
        var otherUserId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _items.GetByIdAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connectionId, UserId = otherUserId, AccessToken = "encrypted", ItemId = "item-1" });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new CreateLinkTokenCommand(user.Id, profileId, connectionId, ""), CancellationToken.None));
    }
}
