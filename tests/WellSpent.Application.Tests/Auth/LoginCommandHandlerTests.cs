using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth;
using WellSpent.Application.Auth.Login;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class LoginCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();

    private LoginCommandHandler CreateHandler() =>
        new(_users, _passwordHasher, _jwt, _refreshTokens, NullLogger<LoginCommandHandler>.Instance);

    private static User ActiveUser(string? hashedPassword = "hashed") => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@example.com",
        HashedPassword = hashedPassword,
        IsActive = true,
        Status = "active",
        Language = "en",
        Currency = "USD",
    };

    [Fact]
    public async Task Success_ReturnsAccessTokenAndRefreshToken()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct", user.HashedPassword!).Returns(true);
        _jwt.GenerateToken(user.Id, AuthConstants.AccessTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(new LoginCommand(user.Email, "correct", false, "device-1"), CancellationToken.None);

        Assert.Equal("token", result.AccessToken);
        Assert.Equal((long)AuthConstants.AccessTokenLifetime.TotalSeconds, result.ExpiresIn);
        Assert.Equal(user.Language, result.Language);
        Assert.Equal(user.Currency, result.Currency);
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));
        await _refreshTokens.Received(1).IssueAsync(
            user.Id, "device-1", Arg.Any<string>(), (int)AuthConstants.DefaultTokenLifetime.TotalSeconds, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RememberMe_IssuesLongerRefreshTokenLifetime()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct", user.HashedPassword!).Returns(true);
        _jwt.GenerateToken(user.Id, AuthConstants.AccessTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(new LoginCommand(user.Email, "correct", true, "device-1"), CancellationToken.None);

        Assert.Equal((long)AuthConstants.AccessTokenLifetime.TotalSeconds, result.ExpiresIn);
        await _refreshTokens.Received(1).IssueAsync(
            user.Id, "device-1", Arg.Any<string>(), (int)AuthConstants.RememberMeTokenLifetime.TotalSeconds, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownEmail_ThrowsGenericInvalidCredentials()
    {
        _users.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "ghost@example.com")));

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand("ghost@example.com", "x", false, "device-1"), CancellationToken.None));
        Assert.Equal("invalid email or password", ex.Message);
    }

    [Fact]
    public async Task WrongPassword_ThrowsGenericInvalidCredentials()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("wrong", user.HashedPassword!).Returns(false);

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "wrong", false, "device-1"), CancellationToken.None));
        Assert.Equal("invalid email or password", ex.Message);
    }

    [Fact]
    public async Task DisabledAccount_ThrowsForbiddenWithRecoveryMessage()
    {
        var user = ActiveUser();
        user.IsActive = false;
        user.Status = "disabled";
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false, "device-1"), CancellationToken.None));
        Assert.Contains("contact support", ex.Message);
    }

    [Fact]
    public async Task InactiveAccount_NotDisabled_ThrowsGenericForbidden()
    {
        var user = ActiveUser();
        user.IsActive = false;
        user.Status = "blocked";
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false, "device-1"), CancellationToken.None));
        Assert.Equal("account is inactive", ex.Message);
    }

    [Fact]
    public async Task OAuthOnlyAccount_ThrowsAppValidationException()
    {
        var user = ActiveUser(hashedPassword: null);
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false, "device-1"), CancellationToken.None));
        Assert.Equal("account uses OAuth login only", ex.Message);
    }
}
