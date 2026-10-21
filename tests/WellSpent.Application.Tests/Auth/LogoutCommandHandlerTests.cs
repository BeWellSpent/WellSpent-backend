using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Auth.Logout;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class LogoutCommandHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();

    private LogoutCommandHandler CreateHandler() => new(_refreshTokens, NullLogger<LogoutCommandHandler>.Instance);

    [Fact]
    public async Task KnownToken_RevokesIt()
    {
        var row = new WellSpent.Domain.Entities.RefreshToken
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DeviceId = "device-1", TokenHash = "irrelevant", ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        _refreshTokens.GetByHashAsync(RefreshTokenGenerator.Hash("raw-token"), Arg.Any<CancellationToken>()).Returns(row);

        var result = await CreateHandler().Handle(new LogoutCommand("raw-token"), CancellationToken.None);

        Assert.Equal(MediatR.Unit.Value, result);
        await _refreshTokens.Received(1).RevokeAsync(row.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownToken_StillSucceeds()
    {
        _refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((WellSpent.Domain.Entities.RefreshToken?)null);

        var result = await CreateHandler().Handle(new LogoutCommand("unknown-token"), CancellationToken.None);

        Assert.Equal(MediatR.Unit.Value, result);
        await _refreshTokens.DidNotReceive().RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
