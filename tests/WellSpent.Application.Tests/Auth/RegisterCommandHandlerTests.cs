using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.AppAttest;
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
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IAppAttestRepository _attestRepo = Substitute.For<IAppAttestRepository>();
    private readonly IAuthRateLimiter _limiter = Substitute.For<IAuthRateLimiter>();
    private bool _attestEnforced;

    private AppAttestGate Gate() => new(_attestRepo,
        Options.Create(new AppAttestOptions { EnforcementEnabled = _attestEnforced, AppId = "app" }), NullLogger<AppAttestGate>.Instance);

    private RegisterCommandHandler CreateHandler(bool captchaEnforced = false)
    {
        var options = Options.Create(new AuthOptions { CaptchaEnforcementEnabled = captchaEnforced, FrontendUrl = "http://localhost:3000" });
        var mailer = new VerificationMailer(_users, _emailSender, options, NullLogger<VerificationMailer>.Instance);
        return new RegisterCommandHandler(_users, _passwordHasher, _captcha, Gate(), _limiter, _jwt, _refreshTokens, mailer, options, NullLogger<RegisterCommandHandler>.Instance);
    }

    private static RegisterCommand ValidCommand() => new(
        "New@Example.com", "Str0ng!Pass", "First", "Last", "US", "CA", "en", "USD", "", "device-1");

    [Fact]
    public async Task Success_NormalizesEmail_HashesPassword_CreatesUser_ReturnsTokens()
    {
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        _passwordHasher.Hash("Str0ng!Pass").Returns("hashed");
        var created = new User { Id = Guid.NewGuid(), Email = "new@example.com", Language = "en", Currency = "USD" };
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(created);
        _jwt.GenerateToken(created.Id, WellSpent.Application.Auth.AuthConstants.AccessTokenLifetime).Returns("token-123");

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("token-123", result.AccessToken);
        Assert.Equal((long)WellSpent.Application.Auth.AuthConstants.AccessTokenLifetime.TotalSeconds, result.ExpiresIn);
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));
        await _users.Received(1).CreateAsync(
            Arg.Is<User>(u => u.Email == "new@example.com" && u.HashedPassword == "hashed"),
            Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).IssueAsync(
            created.Id, "device-1", Arg.Any<string>(), (int)WellSpent.Application.Auth.AuthConstants.DefaultTokenLifetime.TotalSeconds,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
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

    private void NewUserCanBeCreated()
    {
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        _users.CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(c => c.Arg<User>());
    }

    [Fact]
    public async Task AppAttestEnforced_NoProofNoCaptcha_Rejects()
    {
        _attestEnforced = true;
        NewUserCanBeCreated();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(ValidCommand(), CancellationToken.None));

        Assert.Contains("device", ex.Message);
        await _users.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppAttestEnforced_Unavailable_RegistersUnattestedWithTightLimit_SkipsCaptcha()
    {
        _attestEnforced = true;
        NewUserCanBeCreated();

        await CreateHandler(captchaEnforced: true).Handle(ValidCommand() with { ClientIp = "1.2.3.4", AppAttestUnavailable = "unsupported" }, CancellationToken.None);

        await _users.Received().CreateAsync(Arg.Is<User>(u => !u.Attested), Arg.Any<CancellationToken>());
        await _captcha.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _limiter.Received().Hit("register:unattested", "1.2.3.4", 3, TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task AppAttestEnforced_InvalidProof_RejectsEvenWithCaptcha()
    {
        _attestEnforced = true;
        _captcha.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _attestRepo.ConsumeChallengeAsync("ch", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        NewUserCanBeCreated();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler(captchaEnforced: true)
            .Handle(ValidCommand() with { AppAttest = new AppAttestProof("k", "AA==", "ch") }, CancellationToken.None));
    }

    [Fact]
    public async Task AppAttestOff_Unattested_UsesNormalLimit()
    {
        NewUserCanBeCreated();

        await CreateHandler().Handle(ValidCommand() with { ClientIp = "1.2.3.4" }, CancellationToken.None);

        _limiter.Received().Hit("register:attested", "1.2.3.4", 10, TimeSpan.FromHours(1));
        await _users.Received().CreateAsync(Arg.Is<User>(u => !u.Attested), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RateLimited_DoesNotCreateUser()
    {
        NewUserCanBeCreated();
        _limiter.When(l => l.Hit(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>()))
            .Do(_ => throw new TooManyRequestsException("slow down", TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => CreateHandler().Handle(ValidCommand(), CancellationToken.None));
        await _users.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }
}
