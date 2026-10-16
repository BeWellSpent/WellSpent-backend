using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Application.Users;
using WellSpent.Application.Users.ChangeEmail;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Users;

public sealed class ChangeEmailCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<UserMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private ChangeEmailCommandHandler CreateHandler()
    {
        var options = Options.Create(new AuthOptions { FrontendUrl = "http://localhost:3000" });
        var mailer = new VerificationMailer(_users, _emailSender, options, NullLogger<VerificationMailer>.Instance);
        return new ChangeEmailCommandHandler(_users, mailer, Mapper, NullLogger<ChangeEmailCommandHandler>.Instance);
    }

    [Fact]
    public async Task Success_UpdatesEmailAndSendsVerification()
    {
        var userId = Guid.NewGuid();
        var current = new User { Id = userId, Email = "old@example.com" };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(current);
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var updated = new User { Id = userId, Email = "new@example.com", IsVerified = false };
        _users.UpdateEmailAsync(userId, "new@example.com", Arg.Any<CancellationToken>()).Returns(updated);

        var dto = await CreateHandler().Handle(new ChangeEmailCommand(userId, "New@Example.com"), CancellationToken.None);

        Assert.Equal("new@example.com", dto.Email);
        await _emailSender.Received(1).SendAsync("new@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameEmailAsCurrent_Throws()
    {
        var userId = Guid.NewGuid();
        var current = new User { Id = userId, Email = "same@example.com" };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(current);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new ChangeEmailCommand(userId, "same@example.com"), CancellationToken.None));
    }

    [Fact]
    public async Task EmailTakenByAnotherUser_Throws()
    {
        var userId = Guid.NewGuid();
        var current = new User { Id = userId, Email = "old@example.com" };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(current);
        _users.GetByEmailAsync("taken@example.com", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Email = "taken@example.com" });

        await Assert.ThrowsAsync<DuplicateException>(
            () => CreateHandler().Handle(new ChangeEmailCommand(userId, "taken@example.com"), CancellationToken.None));
    }

    [Fact]
    public async Task VerificationSendFailure_DoesNotFailTheChange()
    {
        var userId = Guid.NewGuid();
        var current = new User { Id = userId, Email = "old@example.com" };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(current);
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User>(new NotFoundException("user", "new@example.com")));
        var updated = new User { Id = userId, Email = "new@example.com" };
        _users.UpdateEmailAsync(userId, "new@example.com", Arg.Any<CancellationToken>()).Returns(updated);
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("resend down")));

        var dto = await CreateHandler().Handle(new ChangeEmailCommand(userId, "new@example.com"), CancellationToken.None);

        Assert.Equal("new@example.com", dto.Email);
    }
}
