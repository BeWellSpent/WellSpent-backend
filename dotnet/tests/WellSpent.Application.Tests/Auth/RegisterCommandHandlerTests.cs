using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth.Register;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class RegisterCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ICaptchaVerifier _captcha = Substitute.For<ICaptchaVerifier>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private RegisterCommandHandler CreateHandler(bool captchaEnforced = false)
    {
        var options = Options.Create(new AuthOptions { CaptchaEnforcementEnabled = captchaEnforced, FrontendUrl = "http://localhost:3000" });
        var mailer = new VerificationMailer(_users, _emailSender, options, NullLogger<VerificationMailer>.Instance);
        return new RegisterCommandHandler(_users, _passwordHasher, _captcha, _jwt, mailer, options, NullLogger<RegisterCommandHandler>.Instance);
    }

    private static RegisterCommand ValidCommand() => new(
        "New@Example.com", "Str0ng!Pass", "First", "Last", "US", "CA", "en", "USD", "");

    [Fact]
    public async Task Success_NormalizesEmail_HashesPassword_CreatesUser_ReturnsToken()
    {
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        _passwordHasher.Hash("Str0ng!Pass").Returns("hashed");
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com", Language = "en", Currency = "USD" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);
        _jwt.GenerateToken(created.Id, WellSpent.Application.Auth.AuthConstants.DefaultTokenLifetime).Returns("token-123");

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("token-123", result.AccessToken);
        Assert.Equal((long)WellSpent.Application.Auth.AuthConstants.DefaultTokenLifetime.TotalSeconds, result.ExpiresIn);
        await _users.Received(1).CreateAsync(
            Arg.Is<User>(u => u.Email == "new@example.com" && u.HashedPassword == "hashed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEmail_ThrowsDuplicateException()
    {
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Email = "new@example.com" });

        await Assert.ThrowsAsync<DuplicateException>(() => CreateHandler().Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidEmail_ThrowsAppValidationException()
    {
        var command = ValidCommand() with { Email = "not-an-email" };

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task WeakPassword_ThrowsAppValidationException()
    {
        var command = ValidCommand() with { Password = "weak" };

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CaptchaEnforced_FailedVerification_ThrowsAppValidationException()
    {
        _captcha.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler(captchaEnforced: true).Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task CaptchaEnforced_SuccessfulVerification_Proceeds()
    {
        _captcha.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);

        var result = await CreateHandler(captchaEnforced: true).Handle(ValidCommand(), CancellationToken.None);

        Assert.NotNull(result.AccessToken);
    }

    [Fact]
    public async Task VerificationEmailSendFailure_DoesNotFailRegistration()
    {
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("resend down")));

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.NotNull(result.AccessToken);
    }
}
