using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth.GoogleAuthUrl;
using Xunit;

namespace WellSpent.Application.Tests.Auth;

public sealed class GetGoogleAuthUrlQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsUrlFromClient()
    {
        var google = Substitute.For<IGoogleOAuthClient>();
        google.GetAuthUrl("csrf-state").Returns("https://accounts.google.com/...&state=csrf-state");

        var result = await new GetGoogleAuthUrlQueryHandler(google).Handle(new GetGoogleAuthUrlQuery("csrf-state"), CancellationToken.None);

        Assert.Equal("https://accounts.google.com/...&state=csrf-state", result);
    }
}
