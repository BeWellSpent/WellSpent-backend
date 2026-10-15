using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth.ResendVerificationEmail;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class ResendVerificationEmailCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private ResendVerificationEmailCommandHandler CreateHandler()
    {
        var options = Options.Create(new AuthOptions { FrontendUrl = "http://localhost:3000" });
        var mailer = new VerificationMailer(_users, _emailSender, options, NullLogger<VerificationMailer>.Instance);
        return new ResendVerificationEmailCommandHandler(_users, mailer);
    }

    [Fact]
    public async Task UnknownEmail_SilentlySucceeds_NoEmailSent()
    {
        _users.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "ghost@example.com")));

        await CreateHandler().Handle(new ResendVerificationEmailCommand("ghost@example.com"), CancellationToken.None);

        await _emailSender.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyVerified_SilentlySucceeds_NoEmailSent()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "verified@example.com", IsVerified = true };
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        await CreateHandler().Handle(new ResendVerificationEmailCommand(user.Email), CancellationToken.None);

        await _emailSender.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithinCooldown_ThrowsAppValidationException()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "unverified@example.com", IsVerified = false,
            EmailVerificationLastSentAt = DateTime.UtcNow.AddSeconds(-10),
        };
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new ResendVerificationEmailCommand(user.Email), CancellationToken.None));
    }

    [Fact]
    public async Task PastCooldown_SendsFreshEmail()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "unverified@example.com", IsVerified = false,
            EmailVerificationLastSentAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        await CreateHandler().Handle(new ResendVerificationEmailCommand(user.Email), CancellationToken.None);

        await _emailSender.Received(1).SendAsync(user.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeverSentBefore_SendsEmail()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "unverified@example.com", IsVerified = false, EmailVerificationLastSentAt = null };
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        await CreateHandler().Handle(new ResendVerificationEmailCommand(user.Email), CancellationToken.None);

        await _emailSender.Received(1).SendAsync(user.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
