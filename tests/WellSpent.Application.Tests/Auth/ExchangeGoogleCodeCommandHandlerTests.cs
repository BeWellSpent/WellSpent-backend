using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth;
using WellSpent.Application.Auth.GoogleExchange;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class ExchangeGoogleCodeCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IGoogleOAuthClient _google = Substitute.For<IGoogleOAuthClient>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();

    private ExchangeGoogleCodeCommandHandler CreateHandler() => new(_users, _google, _jwt);

    private static ExchangeGoogleCodeCommand Command() => new("code", "redirect", "en", "USD");

    [Fact]
    public async Task BrandNewUser_CreatesVerifiedUserAndLinksAccount()
    {
        _google.ExchangeCodeAsync("code", Arg.Any<CancellationToken>())
            .Returns(new GoogleUserInfo("google-sub", "new@example.com", "First", "Last"));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderGoogle, "google-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "google:google-sub")));
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com", Language = "en", Currency = "USD" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);
        _jwt.GenerateToken(created.Id, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsNewUser);
        Assert.Equal("token", result.AccessToken);
        await _users.Received(1).MarkVerifiedAsync(created.Id, Arg.Any<CancellationToken>());
        await _users.Received(1).CreateOAuthAccountAsync(
            Arg.Is<OAuthAccount>(o => o.OauthName == AuthConstants.OauthProviderGoogle && o.AccountId == "google-sub"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingEmailPasswordUser_LinksAndAutoVerifies_IsNotNewUser()
    {
        _google.ExchangeCodeAsync("code", Arg.Any<CancellationToken>())
            .Returns(new GoogleUserInfo("google-sub", "existing@example.com", "First", "Last"));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderGoogle, "google-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "google:google-sub")));
        var existing = new User { Id = Guid.NewGuid(), Email = "existing@example.com", Language = "es", Currency = "ARS" };
        _users.GetByEmailAsync("existing@example.com", Arg.Any<CancellationToken>()).Returns(existing);
        _jwt.GenerateToken(existing.Id, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsNewUser);
        Assert.Equal("es", result.Language);
        await _users.Received(1).MarkVerifiedAsync(existing.Id, Arg.Any<CancellationToken>());
        await _users.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturningOAuthUser_ResolvedBySub_SkipsCreation()
    {
        _google.ExchangeCodeAsync("code", Arg.Any<CancellationToken>())
            .Returns(new GoogleUserInfo("google-sub", "existing@example.com", "First", "Last"));
        var existingUserId = Guid.NewGuid();
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderGoogle, "google-sub", Arg.Any<CancellationToken>())
            .Returns(new OAuthAccount { Id = Guid.NewGuid(), UserId = existingUserId, OauthName = "google", AccountId = "google-sub", AccountEmail = "existing@example.com" });
        var existing = new User { Id = existingUserId, Email = "existing@example.com", Language = "en", Currency = "USD" };
        _users.GetByIdAsync(existingUserId, Arg.Any<CancellationToken>()).Returns(existing);
        _jwt.GenerateToken(existingUserId, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsNewUser);
        await _users.DidNotReceive().CreateOAuthAccountAsync(Arg.Any<OAuthAccount>(), Arg.Any<CancellationToken>());
    }
}
