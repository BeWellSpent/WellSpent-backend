using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Users.ChangePassword;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Users;

public sealed class ChangePasswordCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();

    private ChangePasswordCommandHandler CreateHandler() => new(_users, _passwordHasher);

    [Fact]
    public async Task Success_HashesAndStoresNewPassword()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@example.com", HashedPassword = "old-hash" };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("current", "old-hash").Returns(true);
        _passwordHasher.Hash("new-password").Returns("new-hash");

        await CreateHandler().Handle(new ChangePasswordCommand(user.Id, "current", "new-password"), CancellationToken.None);

        await _users.Received(1).UpdatePasswordAsync(user.Id, "new-hash", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrongCurrentPassword_Throws()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@example.com", HashedPassword = "old-hash" };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("wrong", "old-hash").Returns(false);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new ChangePasswordCommand(user.Id, "wrong", "new-password"), CancellationToken.None));
        await _users.DidNotReceive().UpdatePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OAuthOnlyAccount_Throws()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@example.com", HashedPassword = null };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new ChangePasswordCommand(user.Id, "x", "new-password"), CancellationToken.None));
    }
}
