using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth;
using WellSpent.Application.Auth.RefreshToken;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class RefreshTokenCommandHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();

    private RefreshTokenCommandHandler CreateHandler() =>
        new(_refreshTokens, _jwt, NullLogger<RefreshTokenCommandHandler>.Instance);

    private static WellSpent.Domain.Entities.RefreshToken Row(Guid userId, string deviceId, DateTime expiresAt, DateTime? revokedAt = null, int lifetimeSeconds = 86400) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceId = deviceId,
        TokenHash = "irrelevant",
        LifetimeSeconds = lifetimeSeconds,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    [Fact]
    public async Task Success_RotatesAndReturnsNewTokens()
    {
        var userId = Guid.NewGuid();
        var row = Row(userId, "device-1", DateTime.UtcNow.AddDays(1));
        _refreshTokens.GetByHashAsync(RefreshTokenGenerator.Hash("raw-token"), Arg.Any<CancellationToken>()).Returns(row);
        _jwt.GenerateToken(userId, AuthConstants.AccessTokenLifetime).Returns("new-access-token");

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw-token", "device-1"), CancellationToken.None);

        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal((long)AuthConstants.AccessTokenLifetime.TotalSeconds, result.ExpiresIn);
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));
        await _refreshTokens.Received(1).RevokeAsync(row.Id, Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).IssueAsync(userId, "device-1", Arg.Any<string>(), row.LifetimeSeconds, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownToken_ThrowsUnauthorized()
    {
        _refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((WellSpent.Domain.Entities.RefreshToken?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => CreateHandler().Handle(new RefreshTokenCommand("unknown", "device-1"), CancellationToken.None));
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokedToken_ThrowsUnauthorized_AndRevokesEveryOtherTokenForUser()
    {
        var userId = Guid.NewGuid();
        var row = Row(userId, "device-1", DateTime.UtcNow.AddDays(1), revokedAt: DateTime.UtcNow.AddMinutes(-1));
        _refreshTokens.GetByHashAsync(RefreshTokenGenerator.Hash("stolen-token"), Arg.Any<CancellationToken>()).Returns(row);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => CreateHandler().Handle(new RefreshTokenCommand("stolen-token", "device-1"), CancellationToken.None));
        await _refreshTokens.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().IssueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredToken_ThrowsUnauthorized_DoesNotRevokeOtherTokens()
    {
        var userId = Guid.NewGuid();
        var row = Row(userId, "device-1", DateTime.UtcNow.AddMinutes(-1));
        _refreshTokens.GetByHashAsync(RefreshTokenGenerator.Hash("expired-token"), Arg.Any<CancellationToken>()).Returns(row);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => CreateHandler().Handle(new RefreshTokenCommand("expired-token", "device-1"), CancellationToken.None));
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrongDeviceId_ThrowsUnauthorized_DoesNotLeakTokenExistence()
    {
        var userId = Guid.NewGuid();
        var row = Row(userId, "device-1", DateTime.UtcNow.AddDays(1));
        _refreshTokens.GetByHashAsync(RefreshTokenGenerator.Hash("raw-token"), Arg.Any<CancellationToken>()).Returns(row);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => CreateHandler().Handle(new RefreshTokenCommand("raw-token", "device-2"), CancellationToken.None));
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
