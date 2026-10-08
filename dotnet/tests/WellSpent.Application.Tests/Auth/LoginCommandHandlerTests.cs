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

    private LoginCommandHandler CreateHandler() => new(_users, _passwordHasher, _jwt);

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
    public async Task Success_ReturnsToken()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct", user.HashedPassword!).Returns(true);
        _jwt.GenerateToken(user.Id, AuthConstants.DefaultTokenLifetime).Returns("token");

        var result = await CreateHandler().Handle(new LoginCommand(user.Email, "correct", false), CancellationToken.None);

        Assert.Equal("token", result.AccessToken);
        Assert.Equal(user.Language, result.Language);
        Assert.Equal(user.Currency, result.Currency);
    }

    [Fact]
    public async Task RememberMe_UsesLongerLifetime()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct", user.HashedPassword!).Returns(true);
        _jwt.GenerateToken(user.Id, AuthConstants.RememberMeTokenLifetime).Returns("long-token");

        var result = await CreateHandler().Handle(new LoginCommand(user.Email, "correct", true), CancellationToken.None);

        Assert.Equal("long-token", result.AccessToken);
        Assert.Equal((long)AuthConstants.RememberMeTokenLifetime.TotalSeconds, result.ExpiresIn);
    }

    [Fact]
    public async Task UnknownEmail_ThrowsGenericInvalidCredentials()
    {
        _users.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "ghost@example.com")));

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand("ghost@example.com", "x", false), CancellationToken.None));
        Assert.Equal("invalid email or password", ex.Message);
    }

    [Fact]
    public async Task WrongPassword_ThrowsGenericInvalidCredentials()
    {
        var user = ActiveUser();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("wrong", user.HashedPassword!).Returns(false);

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "wrong", false), CancellationToken.None));
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
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false), CancellationToken.None));
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
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false), CancellationToken.None));
        Assert.Equal("account is inactive", ex.Message);
    }

    [Fact]
    public async Task OAuthOnlyAccount_ThrowsAppValidationException()
    {
        var user = ActiveUser(hashedPassword: null);
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new LoginCommand(user.Email, "x", false), CancellationToken.None));
        Assert.Equal("account uses OAuth login only", ex.Message);
    }
}
