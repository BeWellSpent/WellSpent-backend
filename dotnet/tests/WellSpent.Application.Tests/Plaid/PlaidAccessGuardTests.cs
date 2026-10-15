using NSubstitute;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

public sealed class PlaidAccessGuardTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private PlaidAccessGuard CreateGuard() => new(_users);

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };
    private static User NonUsUser() => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "AR", Plan = "lifetime" };

    [Fact]
    public async Task RequireUsAsync_UsUser_ReturnsUser()
    {
        var user = UsUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateGuard().RequireUsAsync(user.Id, CancellationToken.None);

        Assert.Equal(user.Id, result.Id);
    }

    [Fact]
    public async Task RequireUsAsync_NonUsUser_ThrowsForbiddenException()
    {
        var user = NonUsUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateGuard().RequireUsAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RequireProOrLifetimeAsync_PaidUser_ReturnsUser()
    {
        var user = UsUser("pro");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateGuard().RequireProOrLifetimeAsync(user.Id, CancellationToken.None);

        Assert.Equal(user.Id, result.Id);
    }

    [Fact]
    public async Task RequireProOrLifetimeAsync_FreeUser_ThrowsAppValidationException()
    {
        var user = UsUser("free");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateGuard().RequireProOrLifetimeAsync(user.Id, CancellationToken.None));
    }
}
