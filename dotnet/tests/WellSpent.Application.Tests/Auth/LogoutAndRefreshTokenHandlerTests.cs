using WellSpent.Application.Auth.Logout;
using WellSpent.Application.Auth.RefreshToken;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Handle_AlwaysSucceeds()
    {
        var result = await new LogoutCommandHandler().Handle(new LogoutCommand(), CancellationToken.None);
        Assert.Equal(MediatR.Unit.Value, result);
    }
}

public sealed class RefreshTokenCommandHandlerTests
{
    [Fact]
    public async Task Handle_ThrowsNotImplemented()
    {
        await Assert.ThrowsAsync<NotImplementedException>(
            () => new RefreshTokenCommandHandler().Handle(new RefreshTokenCommand("x"), CancellationToken.None));
    }
}
