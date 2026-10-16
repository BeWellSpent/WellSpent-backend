using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth;
using WellSpent.Application.Auth.AppleSignIn;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class SignInWithAppleCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAppleAuthClient _apple = Substitute.For<IAppleAuthClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();

    private SignInWithAppleCommandHandler CreateHandler(string encryptionKey = "") =>
        new(_users, _apple, _crypto, _jwt, Options.Create(new AuthOptions { EncryptionKey = encryptionKey }),
            NullLogger<SignInWithAppleCommandHandler>.Instance);

    private static SignInWithAppleCommand Command(string authCode = "") =>
        new("identity-token", authCode, "First", "Last", "en", "USD");

    [Fact]
    public async Task NoEmailInToken_ThrowsAppValidationException()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("sub", "", true, false));

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidToken_ThrowsAppValidationException()
    {
        _apple.VerifyIdentityTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AppleIdentity>(new Exception("bad signature")));

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task BrandNewUser_CreatesVerifiedUserAndLinksAccount()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "new@example.com", true, false));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "apple:apple-sub")));
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com", Language = "en", Currency = "USD" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);
        var linked = new OAuthAccount { Id = Guid.NewGuid(), UserId = created.Id, OauthName = "apple", AccountId = "apple-sub", AccountEmail = "new@example.com" };
        _users.CreateOAuthAccountAsync(Arg.Any<OAuthAccount>(), Arg.Any<CancellationToken>()).Returns(linked);
        _jwt.GenerateToken(created.Id, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsNewUser);
        await _users.Received(1).MarkVerifiedAsync(created.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingEmailUser_UnverifiedByApple_Rejected()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "existing@example.com", EmailVerified: false, false));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "apple:apple-sub")));
        _users.GetByEmailAsync("existing@example.com", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Email = "existing@example.com", IsActive = true });

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
        await _users.DidNotReceive().CreateOAuthAccountAsync(Arg.Any<OAuthAccount>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingEmailUser_VerifiedByApple_LinksWithoutCreating()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "existing@example.com", EmailVerified: true, false));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "apple:apple-sub")));
        var existing = new User { Id = Guid.NewGuid(), Email = "existing@example.com", IsActive = true, Language = "en", Currency = "USD" };
        _users.GetByEmailAsync("existing@example.com", Arg.Any<CancellationToken>()).Returns(existing);
        var linked = new OAuthAccount { Id = Guid.NewGuid(), UserId = existing.Id, OauthName = "apple", AccountId = "apple-sub", AccountEmail = existing.Email };
        _users.CreateOAuthAccountAsync(Arg.Any<OAuthAccount>(), Arg.Any<CancellationToken>()).Returns(linked);
        _jwt.GenerateToken(existing.Id, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsNewUser);
        await _users.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingInactiveUser_LinkPath_Rejected()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "existing@example.com", EmailVerified: true, false));
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OAuthAccount>(new NotFoundException("oauth_account", "apple:apple-sub")));
        _users.GetByEmailAsync("existing@example.com", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Email = "existing@example.com", IsActive = false });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task ReturningAppleUser_InactiveAccount_Rejected()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "existing@example.com", true, false));
        var userId = Guid.NewGuid();
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "apple", AccountId = "apple-sub", AccountEmail = "existing@example.com" });
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = userId, Email = "existing@example.com", IsActive = false });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshTokenExchangeFailure_IsSwallowed_SignInStillSucceeds()
    {
        _apple.VerifyIdentityTokenAsync("identity-token", Arg.Any<CancellationToken>())
            .Returns(new AppleIdentity("apple-sub", "existing@example.com", true, false));
        var userId = Guid.NewGuid();
        _users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, "apple-sub", Arg.Any<CancellationToken>())
            .Returns(new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "apple", AccountId = "apple-sub", AccountEmail = "existing@example.com" });
        var user = new User { Id = userId, Email = "existing@example.com", IsActive = true, Language = "en", Currency = "USD" };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _apple.ExchangeCodeAsync("auth-code", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new AppleKeyNotConfiguredException()));
        _jwt.GenerateToken(userId, AuthConstants.RememberMeTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(Command(authCode: "auth-code"), CancellationToken.None);

        Assert.Equal("token", result.AccessToken);
    }
}
