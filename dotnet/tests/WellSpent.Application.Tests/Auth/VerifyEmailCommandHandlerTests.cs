using NSubstitute;
using WellSpent.Application.Auth.VerifyEmail;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class VerifyEmailCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private VerifyEmailCommandHandler CreateHandler() => new(_users);

    [Fact]
    public async Task Success_MarksUserVerified()
    {
        var token = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), Email = "x@example.com", EmailVerificationExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        _users.GetByVerificationTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);

        await CreateHandler().Handle(new VerifyEmailCommand(token.ToString()), CancellationToken.None);

        await _users.Received(1).MarkVerifiedAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedToken_ThrowsGenericInvalidToken()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new VerifyEmailCommand("not-a-guid"), CancellationToken.None));
        Assert.Equal("invalid or expired verification token", ex.Message);
    }

    [Fact]
    public async Task UnknownToken_ThrowsGenericInvalidToken()
    {
        var token = Guid.NewGuid();
        _users.GetByVerificationTokenAsync(token, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", token.ToString())));

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new VerifyEmailCommand(token.ToString()), CancellationToken.None));
        Assert.Equal("invalid or expired verification token", ex.Message);
    }

    [Fact]
    public async Task ExpiredToken_ThrowsGenericInvalidToken()
    {
        var token = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), Email = "x@example.com", EmailVerificationExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        _users.GetByVerificationTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new VerifyEmailCommand(token.ToString()), CancellationToken.None));
    }

    [Fact]
    public async Task NullExpiry_ThrowsGenericInvalidToken()
    {
        var token = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), Email = "x@example.com", EmailVerificationExpiresAt = null };
        _users.GetByVerificationTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new VerifyEmailCommand(token.ToString()), CancellationToken.None));
    }
}
